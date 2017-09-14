﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace GraphView
{
    internal enum GremlinVariableType
    {
        Vertex,
        Edge,
        Scalar,
        Table,
        VertexProperty,
        Property,
        NULL,
        Undefined
    }
     
    internal abstract class GremlinVariable
    {
        protected string VariableName;
        public List<string> Labels { get; set; }
        public List<string> ProjectedProperties { get; set; }

        public bool NeedFilter;

        public GremlinVariable()
        {
            this.Labels = new List<string>();
            this.ProjectedProperties = new List<string>();
            this.NeedFilter = false;
        }

        internal virtual void AlignSelectQueryBlocks(List<WSelectQueryBlock> selectQueryBlocks)
        {
            this.ProjectedProperties.Sort();
            WSelectElement value;
            for (int index = 0; index < selectQueryBlocks.Count; index++)
            {
                Dictionary<string, WSelectElement> projectionMap = new Dictionary<string, WSelectElement>();

                // defaultProjection
                value = selectQueryBlocks[index].SelectElements[0].Copy();

                foreach (WSelectElement selectElement in selectQueryBlocks[index].SelectElements)
                {
                    projectionMap[(selectElement as WSelectScalarExpression).ColumnName] = selectElement;
                }
                selectQueryBlocks[index].SelectElements.Clear();

                // defaultProjection
                selectQueryBlocks[index].SelectElements.Add(SqlUtil.GetSelectScalarExpr((value as WSelectScalarExpression).SelectExpr, this.DefaultProperty()));
                foreach (string property in this.ProjectedProperties)
                {
                    selectQueryBlocks[index].SelectElements.Add(
                        projectionMap.TryGetValue(property, out value)
                        ? value : SqlUtil.GetSelectScalarExpr(SqlUtil.GetValueExpr(null), property));
                }
            }
        }

        internal virtual GremlinVariableType GetVariableType()
        {
            throw new NotImplementedException();
        }

        internal virtual bool Populate(string property, string label = null)
        {
            if (label == null)
            {
                if (!this.ProjectedProperties.Contains(property))
                {
                    this.ProjectedProperties.Add(property);
                }
                return true;
            }
            else if (this.Labels.Contains(label))
            {
                if (!this.ProjectedProperties.Contains(property))
                {
                    this.ProjectedProperties.Add(property);
                }
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// This function is used for populate local path in a subquery,
        /// so it should be overrided by any GremlinVariable which has Subquery.
        /// In the base case, it will do nothing.
        /// These variables are: Local/Optional/Union/Choose/Repeat.
        /// Note: Although Coalesce/FlatMap/Map/Project have subquery, these steps are treated as one step, so we needn't override this function
        /// </summary>
        internal virtual void PopulateLocalPath() {}

        /// <summary>
        /// This function is used for populate property for each step in a path
        /// If a step has a subquery, then this funcion will populate the property for each step in the subquery,
        /// so it should be overrided by any GremlinVariable which has Subquery.
        /// In the base case, it will populate the property for itself. 
        /// This function should be overrided by any GremlinVariable which has Subquery.
        /// These variables are: Local/Optional/Union/Choose/Repeat.
        /// Note: Although Coalesce/FlatMap/Map/Project have subquery, these steps are treated as one step, so we needn't override this function
        /// </summary>
        internal virtual bool PopulateStepProperty(string property, string label = null)
        {
            return this.Populate(property, label);
        }

        internal virtual GremlinVariableProperty GetVariableProperty(string property)
        {
            if (property != GremlinKeyword.Path)
            {
                this.Populate(property, null);
            }
            return new GremlinVariableProperty(this, property);
        }

        internal virtual string GetVariableName()
        {
            if (this.VariableName == null)
            {
                throw new QueryCompilationException("VariableName can't be null");
            }
            return this.VariableName;
        }

        internal WBooleanExpression CreateBooleanExpression(GremlinVariableProperty variableProperty, object valuesOrPredicate)
        {
            if (valuesOrPredicate is string || GremlinUtil.IsNumber(valuesOrPredicate) || valuesOrPredicate is bool)
            {
                WScalarExpression firstExpr = variableProperty.ToScalarExpression();
                WScalarExpression secondExpr = SqlUtil.GetValueExpr(valuesOrPredicate);
                return SqlUtil.GetEqualBooleanComparisonExpr(firstExpr, secondExpr);
            }
            if (valuesOrPredicate is Predicate)
            {
                WScalarExpression firstExpr = variableProperty.ToScalarExpression();
                WScalarExpression secondExpr = SqlUtil.GetValueExpr((valuesOrPredicate as Predicate).Value);
                return SqlUtil.GetBooleanComparisonExpr(firstExpr, secondExpr, valuesOrPredicate as Predicate);
            }
            throw new ArgumentException();
        }

        internal WBooleanExpression GetWherePredicate(GremlinToSqlContext currentContext, GremlinVariable firstVar, Predicate predicate, TraversalRing traversalRing)
        {
            AndPredicate andPredicate = predicate as AndPredicate;
            if (andPredicate != null)
            {
                List<WBooleanExpression> booleanList = new List<WBooleanExpression>();
                foreach (var p in andPredicate.PredicateList)
                {
                    booleanList.Add(GetWherePredicate(currentContext, firstVar, p, traversalRing));
                }
                return SqlUtil.ConcatBooleanExprWithAnd(booleanList);
            }

            OrPredicate orPredicate = predicate as OrPredicate;
            if (orPredicate != null)
            {
                List<WBooleanExpression> booleanList = new List<WBooleanExpression>();
                foreach (var p in orPredicate.PredicateList)
                {
                    booleanList.Add(GetWherePredicate(currentContext, firstVar, p, traversalRing));
                }
                return SqlUtil.ConcatBooleanExprWithOr(booleanList);
            }

            var selectKeys = new List<string>() { predicate.Value as string };
            var selectTraversal = new List<GraphTraversal>() { traversalRing.Next() };
            var selectVar = GenerateSelectVariable(currentContext, GremlinKeyword.Pop.Last, selectKeys, selectTraversal);
            var firstExpr = firstVar.DefaultProjection().ToScalarExpression();
            var secondExpr = selectVar.DefaultProjection().ToScalarExpression();
            return SqlUtil.GetBooleanComparisonExpr(firstExpr, secondExpr, predicate);
        }

        /// <summary>
        /// //This function is used for the algorithm of Repeat Step 
        /// </summary>
        /// <returns></returns>
        internal virtual List<GremlinVariable> FetchAllVars()
        {
            return new List<GremlinVariable> { this };
        }

        internal virtual GremlinVariableProperty DefaultProjection()
        {
            string property = this.DefaultProperty();
            return new GremlinVariableProperty(this, property);
        }

        internal virtual string DefaultProperty()
        {
            switch (GetVariableType())
            {
                case GremlinVariableType.Edge:
                case GremlinVariableType.Vertex:
                    return GremlinKeyword.Star;
                default:
                    return GremlinKeyword.TableDefaultColumnName;
            }
        }

        internal virtual WScalarExpression ToStepScalarExpr()
        {
            return this.ToCompose1();
        }

        internal virtual WFunctionCall ToCompose1()
        {
            List<WScalarExpression> parameters = new List<WScalarExpression>();
            // parameters.Add(SqlUtil.GetValueExpr(GremlinKeyword.TableDefaultColumnName));
            parameters.Add(DefaultProjection().ToScalarExpression());
            parameters.Add(SqlUtil.GetValueExpr(GremlinKeyword.TableDefaultColumnName));
            foreach (var projectProperty in ProjectedProperties)
            {
                parameters.Add(GetVariableProperty(projectProperty).ToScalarExpression());
                parameters.Add(SqlUtil.GetValueExpr(projectProperty));
            }
            return SqlUtil.GetFunctionCall(GremlinKeyword.func.Compose1, parameters);
        }

        /// <summary>
        /// Step Funtions
        /// </summary>
        internal virtual void AddE(GremlinToSqlContext currentContext, string edgeLabel, List<GremlinProperty> edgeProperties, GremlinToSqlContext fromContext, GremlinToSqlContext toContext)
        {
            this.NeedFilter = true;
            GremlinAddETableVariable newTableVariable = new GremlinAddETableVariable(this, edgeLabel, edgeProperties, fromContext, toContext);
            currentContext.VariableList.Add(newTableVariable);
            currentContext.TableReferencesInFromClause.Add(newTableVariable);
            currentContext.SetPivotVariable(newTableVariable);
        }

        internal virtual void AddV(GremlinToSqlContext currentContext, string vertexLabel, List<GremlinProperty> propertyKeyValues)
        {
            this.NeedFilter = true;
            GremlinAddVVariable newVariable = new GremlinAddVVariable(vertexLabel, propertyKeyValues);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void Aggregate(GremlinToSqlContext currentContext, string sideEffectKey, GremlinToSqlContext projectContext)
        {
            this.NeedFilter = true;
            GremlinAggregateVariable newVariable = new GremlinAggregateVariable(projectContext, sideEffectKey);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
        }

        internal virtual void And(GremlinToSqlContext currentContext, List<GremlinToSqlContext> andContexts)
        {
            List<WBooleanExpression> booleanExprList = new List<WBooleanExpression>();
            foreach (var context in andContexts)
            {
                currentContext.AllTableVariablesInWhereClause.AddRange(context.FetchAllTableVars());
                booleanExprList.Add(context.ToSqlBoolean());
            }
            WBooleanExpression newPredicate = SqlUtil.ConcatBooleanExprWithAnd(booleanExprList);

            if (this.NeedFilter)
            {
                GremlinFilterVariable newVariable = new GremlinFilterVariable(newPredicate);
                currentContext.VariableList.Add(newVariable);
                currentContext.TableReferencesInFromClause.Add(newVariable);
            }
            else
            {
                currentContext.AddPredicate(newPredicate);
            }
            
        }

        internal virtual void As(GremlinToSqlContext currentContext, List<string> labels)
        {
            foreach (var label in labels)
            {
                if (!this.Labels.Contains(label))
                {
                    this.Labels.Add(label);
                }
            }
        }

        internal virtual void Barrier(GremlinToSqlContext currentContext)
        {
            GremlinBarrierVariable newVariable = new GremlinBarrierVariable();
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
        }

        internal virtual void Both(GremlinToSqlContext currentContext, List<string> edgeLabels)
        {
            GremlinVertexToBothEdgeVariable bothEdgeTable = new GremlinVertexToBothEdgeVariable(this);
            currentContext.VariableList.Add(bothEdgeTable);
            currentContext.TableReferencesInFromClause.Add(bothEdgeTable);
            currentContext.AddLabelPredicateForEdge(bothEdgeTable, edgeLabels);

            GremlinEdgeToOtherVertexVariable otherSourceVertex = new GremlinEdgeToOtherVertexVariable(bothEdgeTable);
            currentContext.VariableList.Add(otherSourceVertex);
            currentContext.TableReferencesInFromClause.Add(otherSourceVertex);
            currentContext.SetPivotVariable(otherSourceVertex);
        }

        internal virtual void Both1(GremlinToSqlContext currentContext, List<string> edgeLabels)
        {
            if (this.GetVariableType() == GremlinVariableType.Vertex)
            {
                GremlinVertexToBothEdgeVariable bothEdgeTable = new GremlinVertexToBothEdgeVariable(this);
                currentContext.VariableList.Add(bothEdgeTable);
                currentContext.TableReferencesInFromClause.Add(bothEdgeTable);
                currentContext.AddLabelPredicateForEdge(bothEdgeTable, edgeLabels);

                GremlinFreeVertexVariable otherSourceVertex = new GremlinFreeVertexVariable();
                currentContext.VariableList.Add(otherSourceVertex);
                currentContext.TableReferencesInFromClause.Add(otherSourceVertex);

                GremlinVariableProperty edgeSinkVProperty = bothEdgeTable.GetVariableProperty(GremlinKeyword.EdgeSinkV);
                GremlinVariableProperty edgeSourceVProperty = bothEdgeTable.GetVariableProperty(GremlinKeyword.EdgeSourceV);
                GremlinVariableProperty v1NodeIDProperty = this.GetVariableProperty(GremlinKeyword.NodeID);
                GremlinVariableProperty v2NodeIDProperty = otherSourceVertex.GetVariableProperty(GremlinKeyword.NodeID);

                WBooleanExpression edgeToSinkVertexExpr1 = SqlUtil.GetEqualBooleanComparisonExpr(edgeSinkVProperty.ToScalarExpression(), v1NodeIDProperty.ToScalarExpression());
                WBooleanExpression edgeToSourceVertexExpr1 = SqlUtil.GetEqualBooleanComparisonExpr(edgeSourceVProperty.ToScalarExpression(), v2NodeIDProperty.ToScalarExpression());
                WBooleanExpression edgeToSinkVertexExpr2 = SqlUtil.GetEqualBooleanComparisonExpr(edgeSinkVProperty.ToScalarExpression(), v2NodeIDProperty.ToScalarExpression());
                WBooleanExpression edgeToSourceVertexExpr2 = SqlUtil.GetEqualBooleanComparisonExpr(edgeSourceVProperty.ToScalarExpression(), v1NodeIDProperty.ToScalarExpression());

                WBooleanBinaryExpression expr1 =
                    SqlUtil.GetAndBooleanBinaryExpr(edgeToSinkVertexExpr1, edgeToSourceVertexExpr1);
                WBooleanBinaryExpression expr2 =
                    SqlUtil.GetAndBooleanBinaryExpr(edgeToSinkVertexExpr2, edgeToSourceVertexExpr2);

                currentContext.AddPredicate(SqlUtil.GetOrBooleanBinaryExpr(expr1, expr2));

                currentContext.SetPivotVariable(otherSourceVertex);
            }
            else
            {
                GremlinVertexToBothEdgeVariable bothEdgeTable = new GremlinVertexToBothEdgeVariable(this);
                currentContext.VariableList.Add(bothEdgeTable);
                currentContext.TableReferencesInFromClause.Add(bothEdgeTable);
                currentContext.AddLabelPredicateForEdge(bothEdgeTable, edgeLabels);

                GremlinEdgeToOtherVertexVariable otherSourceVertex = new GremlinEdgeToOtherVertexVariable(bothEdgeTable);
                currentContext.VariableList.Add(otherSourceVertex);
                currentContext.TableReferencesInFromClause.Add(otherSourceVertex);
                currentContext.SetPivotVariable(otherSourceVertex);
            }

        }

        internal virtual void BothE(GremlinToSqlContext currentContext, List<string> edgeLabels)
        {
            GremlinVertexToBothEdgeVariable bothEdgeTable = new GremlinVertexToBothEdgeVariable(this);
            currentContext.VariableList.Add(bothEdgeTable);
            currentContext.TableReferencesInFromClause.Add(bothEdgeTable);
            currentContext.AddLabelPredicateForEdge(bothEdgeTable, edgeLabels);

            currentContext.SetPivotVariable(bothEdgeTable);
        }

        internal virtual void BothV(GremlinToSqlContext currentContext)
        {
            GremlinEdgeToBothVertexVariable bothSourceVertex = new GremlinEdgeToBothVertexVariable(this);

            currentContext.VariableList.Add(bothSourceVertex);
            currentContext.TableReferencesInFromClause.Add(bothSourceVertex);
            currentContext.SetPivotVariable(bothSourceVertex);
        }

        internal virtual void BothV1(GremlinToSqlContext currentContext)
        {
            if (this.GetVariableType() == GremlinVariableType.Edge)
            {
                GremlinFreeVertexVariable bothSourceVertex = new GremlinFreeVertexVariable();

                currentContext.VariableList.Add(bothSourceVertex);
                currentContext.TableReferencesInFromClause.Add(bothSourceVertex);

                GremlinVariableProperty edgeSinkVProperty = this.GetVariableProperty(GremlinKeyword.EdgeSinkV);
                GremlinVariableProperty edgeSourceVProperty = this.GetVariableProperty(GremlinKeyword.EdgeSourceV);
                GremlinVariableProperty vNodeIDProperty = bothSourceVertex.GetVariableProperty(GremlinKeyword.NodeID);

                WBooleanExpression edgeToSinkVertexExpr =
                    SqlUtil.GetEqualBooleanComparisonExpr(edgeSinkVProperty.ToScalarExpression(),
                        vNodeIDProperty.ToScalarExpression());
                WBooleanExpression edgeToSourceVertexExpr =
                    SqlUtil.GetEqualBooleanComparisonExpr(edgeSourceVProperty.ToScalarExpression(),
                        vNodeIDProperty.ToScalarExpression());
                WBooleanBinaryExpression edgeToBothVertexExpr =
                    SqlUtil.GetOrBooleanBinaryExpr(edgeToSinkVertexExpr, edgeToSourceVertexExpr);

                currentContext.AddPredicate(edgeToBothVertexExpr);

                currentContext.SetPivotVariable(bothSourceVertex);
            }
            else
            {
                GremlinEdgeToBothVertexVariable bothSourceVertex = new GremlinEdgeToBothVertexVariable(this);

                currentContext.VariableList.Add(bothSourceVertex);
                currentContext.TableReferencesInFromClause.Add(bothSourceVertex);
                currentContext.SetPivotVariable(bothSourceVertex);
            }

        }

        internal virtual void Cap(GremlinToSqlContext currentContext, List<string> sideEffectKeys)
        {
            List<GremlinVariable> sideEffectVariables = currentContext.GetSideEffectVariables();
            GremlinCapVariable newVariable = new GremlinCapVariable(currentContext.Duplicate(), sideEffectVariables, sideEffectKeys);
            currentContext.Reset();
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void Choose(GremlinToSqlContext currentContext, GremlinToSqlContext predicateContext, GremlinToSqlContext trueChoiceContext, GremlinToSqlContext falseChoiceContext)
        {
            GremlinChooseVariable newVariable = new GremlinChooseVariable(predicateContext, trueChoiceContext, falseChoiceContext);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void Choose(GremlinToSqlContext currentContext, GremlinToSqlContext choiceContext, Dictionary<object, GremlinToSqlContext> options)
        {
            GremlinChooseVariable newVariable = new GremlinChooseVariable(choiceContext, options);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void Coalesce(GremlinToSqlContext currentContext, List<GremlinToSqlContext> coalesceContextList)
        {
            GremlinCoalesceVariable newVariable = new GremlinCoalesceVariable(coalesceContextList, GremlinUtil.GetContextListType(coalesceContextList));
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void Coin(GremlinToSqlContext currentContext, double probability)
        {
            GremlinCoinVariable newVariable = new GremlinCoinVariable(probability);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
        }

        internal virtual void Constant(GremlinToSqlContext currentContext, object value)
        {
            GremlinConstantVariable newVariable = new GremlinConstantVariable(value);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void Count(GremlinToSqlContext currentContext)
        {
            GremlinCountVariable newVariable = new GremlinCountVariable(currentContext.Duplicate());
            currentContext.Reset();
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void CountLocal(GremlinToSqlContext currentContext)
        {
            GremlinCountLocalVariable newVariable = new GremlinCountLocalVariable(this);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void CyclicPath(GremlinToSqlContext currentContext, string fromLabel = null, string toLabel = null)
        {
            GremlinCyclicPathVariable newVariable = new GremlinCyclicPathVariable(GeneratePath(currentContext, fromLabel, toLabel));
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
        }

        internal virtual void Dedup(GremlinToSqlContext currentContext, List<string> dedupLabels, GraphTraversal dedupTraversal, GremlinKeyword.Scope scope)
        {
            List<GremlinVariable> dedupVariables = new List<GremlinVariable>();
            foreach (var dedupLabel in dedupLabels)
            {
                dedupVariables.Add(GenerateSelectVariable(currentContext, GremlinKeyword.Pop.Last, new List<string> { dedupLabel}, new List<GraphTraversal>() {dedupTraversal.Copy()}));
            }

            dedupTraversal.GetStartOp().InheritedVariableFromParent(currentContext);
            GremlinToSqlContext dedupContext = dedupTraversal.GetEndOp().GetContext();

            GremlinDedupVariable newVariable = new GremlinDedupVariable(this, dedupVariables, dedupContext, scope);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            if (scope == GremlinKeyword.Scope.Local)
            {
                currentContext.SetPivotVariable(newVariable);
            }
        }

        internal virtual void Drop(GremlinToSqlContext currentContext)
        {
            this.NeedFilter = true;
            GremlinDropVariable dropVariable = new GremlinDropVariable(this);
            currentContext.VariableList.Add(dropVariable);
            currentContext.TableReferencesInFromClause.Add(dropVariable);
            currentContext.SetPivotVariable(dropVariable);
        }

        internal virtual void FlatMap(GremlinToSqlContext currentContext, GremlinToSqlContext flatMapContext)
        {
            GremlinFlatMapVariable flatMapVariable = new GremlinFlatMapVariable(flatMapContext, flatMapContext.PivotVariable.GetVariableType());
            currentContext.VariableList.Add(flatMapVariable);
            currentContext.TableReferencesInFromClause.Add(flatMapVariable);
            currentContext.SetPivotVariable(flatMapVariable);
        }

        internal virtual void Fold(GremlinToSqlContext currentContext)
        {
            GremlinFoldVariable newVariable  = new GremlinFoldVariable(currentContext.Duplicate());
            currentContext.Reset();
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void Group(GremlinToSqlContext currentContext, string sideEffectKey, GremlinToSqlContext groupByContext,
            GremlinToSqlContext projectByContext, bool isProjectingACollection)
        {
            this.NeedFilter = true;
            //TODO: clear history of path
            GremlinGroupVariable newVariable = new GremlinGroupVariable(this, sideEffectKey, groupByContext, projectByContext, isProjectingACollection);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            if (sideEffectKey == null)
            {
                currentContext.SetPivotVariable(newVariable);
            }
        }

        internal virtual void Has(GremlinToSqlContext currentContext, string propertyKey)
        {
            GraphTraversal traversal = GraphTraversal.__().Properties(propertyKey);
            traversal.GetStartOp().InheritedVariableFromParent(currentContext);
            WBooleanExpression newPredicate =
                SqlUtil.GetExistPredicate(traversal.GetEndOp().GetContext().ToSelectQueryBlock());

            if (this.NeedFilter)
            {
                GremlinFilterVariable newVariable = new GremlinFilterVariable(newPredicate);
                currentContext.VariableList.Add(newVariable);
                currentContext.TableReferencesInFromClause.Add(newVariable);
            }
            else
            {
                currentContext.AddPredicate(newPredicate);
            }
        }

        internal virtual void Has(GremlinToSqlContext currentContext, string propertyKey, object valuesOrPredicate)
        {
            WBooleanExpression newPredicate =
                CreateBooleanExpression(GetVariableProperty(propertyKey), valuesOrPredicate);

            if (this.NeedFilter)
            {
                GremlinFilterVariable newVariable = new GremlinFilterVariable(newPredicate);
                currentContext.VariableList.Add(newVariable);
                currentContext.TableReferencesInFromClause.Add(newVariable);
            }
            else
            {
                currentContext.AddPredicate(newPredicate);
            }
        }

        internal virtual void HasIdOrLabel(GremlinToSqlContext currentContext, GremlinHasType hasType, List<object> valuesOrPredicates)
        {
            GremlinVariableProperty variableProperty = hasType == GremlinHasType.HasId
                ? GetVariableProperty(GremlinKeyword.DefaultId)
                : GetVariableProperty(GremlinKeyword.Label);
            List <WBooleanExpression> booleanExprList = new List<WBooleanExpression>();
            foreach (var valuesOrPredicate in valuesOrPredicates)
            {
                booleanExprList.Add(CreateBooleanExpression(variableProperty, valuesOrPredicate));
            }
            WBooleanExpression newPredicate = SqlUtil.ConcatBooleanExprWithOr(booleanExprList);

            if (this.NeedFilter)
            {
                GremlinFilterVariable newVariable = new GremlinFilterVariable(newPredicate);
                currentContext.VariableList.Add(newVariable);
                currentContext.TableReferencesInFromClause.Add(newVariable);
            }
            else
            {
                currentContext.AddPredicate(newPredicate);
            }
        }

        /// <summary>
        /// Only valid for VertexProperty
        /// </summary>
        internal virtual void HasKeyOrValue(GremlinToSqlContext currentContext, GremlinHasType hasType, List<object> valuesOrPredicates)
        {
            GraphTraversal traversal = hasType == GremlinHasType.HasKey ? GraphTraversal.__().Key() : GraphTraversal.__().Value();
            traversal.GetStartOp().InheritedVariableFromParent(currentContext);
            GremlinToSqlContext existContext = traversal.GetEndOp().GetContext();

            List<WBooleanExpression> booleanExprList = new List<WBooleanExpression>();
            GremlinVariableProperty defaultVariableProperty = existContext.PivotVariable.DefaultProjection();
            foreach (var valuesOrPredicate in valuesOrPredicates)
            {
                booleanExprList.Add(CreateBooleanExpression(defaultVariableProperty, valuesOrPredicate));
            }
            existContext.AddPredicate(SqlUtil.ConcatBooleanExprWithOr(booleanExprList));
            WBooleanExpression newPredicate = SqlUtil.GetExistPredicate(existContext.ToSelectQueryBlock());

            if (this.NeedFilter)
            {
                GremlinFilterVariable newVariable = new GremlinFilterVariable(newPredicate);
                currentContext.VariableList.Add(newVariable);
                currentContext.TableReferencesInFromClause.Add(newVariable);
            }
            else
            {
                currentContext.AddPredicate(newPredicate);
            }
        }

        internal virtual void HasNot(GremlinToSqlContext currentContext, string propertyKey)
        {
            GraphTraversal traversal = GraphTraversal.__().Properties(propertyKey);
            traversal.GetStartOp().InheritedVariableFromParent(currentContext);
            WBooleanExpression newPredicate =
                SqlUtil.GetNotExistPredicate(traversal.GetEndOp().GetContext().ToSelectQueryBlock());

            if (this.NeedFilter)
            {
                GremlinFilterVariable newVariable = new GremlinFilterVariable(newPredicate);
                currentContext.VariableList.Add(newVariable);
                currentContext.TableReferencesInFromClause.Add(newVariable);
            }
            else
            {
                currentContext.AddPredicate(newPredicate);
            }
        }

        internal virtual void Id(GremlinToSqlContext currentContext)
        {
            GremlinIdVariable newVariable = new GremlinIdVariable(this);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void In(GremlinToSqlContext currentContext, List<string> edgeLabels)
        {
            GremlinVertexToBackwardEdgeVariable inEdgeTable = new GremlinVertexToBackwardEdgeVariable(this);
            currentContext.VariableList.Add(inEdgeTable);
            currentContext.TableReferencesInFromClause.Add(inEdgeTable);
            currentContext.AddLabelPredicateForEdge(inEdgeTable, edgeLabels);

            GremlinEdgeToSourceVertexVariable outSourceVertex = new GremlinEdgeToSourceVertexVariable(inEdgeTable);
            currentContext.VariableList.Add(outSourceVertex);
            currentContext.TableReferencesInFromClause.Add(outSourceVertex);

            currentContext.SetPivotVariable(outSourceVertex);
        }

        internal virtual void In1(GremlinToSqlContext currentContext, List<string> edgeLabels)
        {
            if (this.GetVariableType() == GremlinVariableType.Vertex)
            {
                GremlinVertexToBackwardEdgeVariable inEdgeTable = new GremlinVertexToBackwardEdgeVariable(this);
                currentContext.VariableList.Add(inEdgeTable);
                currentContext.TableReferencesInFromClause.Add(inEdgeTable);
                currentContext.AddLabelPredicateForEdge(inEdgeTable, edgeLabels);

                GremlinFreeVertexVariable outSourceVertex = new GremlinFreeVertexVariable();
                currentContext.VariableList.Add(outSourceVertex);
                currentContext.TableReferencesInFromClause.Add(outSourceVertex);

                GremlinVariableProperty edgeSourceVProperty =
                    inEdgeTable.GetVariableProperty(GremlinKeyword.EdgeSourceV);
                GremlinVariableProperty vNodeIDProperty = outSourceVertex.GetVariableProperty(GremlinKeyword.NodeID);
                WBooleanExpression edgeToSourceVertexExpr =
                    SqlUtil.GetEqualBooleanComparisonExpr(edgeSourceVProperty.ToScalarExpression(),
                        vNodeIDProperty.ToScalarExpression());
                currentContext.AddPredicate(edgeToSourceVertexExpr);

                currentContext.SetPivotVariable(outSourceVertex);
            }
            else
            {
                GremlinVertexToBackwardEdgeVariable inEdgeTable = new GremlinVertexToBackwardEdgeVariable(this);
                currentContext.VariableList.Add(inEdgeTable);
                currentContext.TableReferencesInFromClause.Add(inEdgeTable);
                currentContext.AddLabelPredicateForEdge(inEdgeTable, edgeLabels);

                GremlinEdgeToSourceVertexVariable outSourceVertex = new GremlinEdgeToSourceVertexVariable(inEdgeTable);
                currentContext.VariableList.Add(outSourceVertex);
                currentContext.TableReferencesInFromClause.Add(outSourceVertex);

                currentContext.SetPivotVariable(outSourceVertex);
            }
        }

        internal virtual void InE(GremlinToSqlContext currentContext, List<string> edgeLabels)
        {
            GremlinVertexToBackwardEdgeVariable inEdgeTable = new GremlinVertexToBackwardEdgeVariable(this);
            currentContext.VariableList.Add(inEdgeTable);
            currentContext.TableReferencesInFromClause.Add(inEdgeTable);
            currentContext.AddLabelPredicateForEdge(inEdgeTable, edgeLabels);

            currentContext.SetPivotVariable(inEdgeTable);
        }

        internal virtual void Inject(GremlinToSqlContext currentContext, object injection)
        {
            this.NeedFilter = true;
            GremlinInjectVariable injectVar = new GremlinInjectVariable(this, injection);
            currentContext.VariableList.Add(injectVar);
            currentContext.TableReferencesInFromClause.Add(injectVar);
        }

        internal virtual void InV(GremlinToSqlContext currentContext)
        {
            GremlinEdgeToSinkVertexVariable inVertex = new GremlinEdgeToSinkVertexVariable(this);
            currentContext.VariableList.Add(inVertex);
            currentContext.TableReferencesInFromClause.Add(inVertex);
            currentContext.SetPivotVariable(inVertex);
        }

        internal virtual void InV1(GremlinToSqlContext currentContext)
        {
            if (this.GetVariableType() == GremlinVariableType.Edge)
            {
                GremlinFreeVertexVariable inVertex = new GremlinFreeVertexVariable();
                currentContext.VariableList.Add(inVertex);
                currentContext.TableReferencesInFromClause.Add(inVertex);

                GremlinVariableProperty edgeSinkVProperty = this.GetVariableProperty(GremlinKeyword.EdgeSinkV);
                GremlinVariableProperty vNodeIDProperty = inVertex.GetVariableProperty(GremlinKeyword.NodeID);
                WBooleanExpression edgeToSinkVertexExp = SqlUtil.GetEqualBooleanComparisonExpr(edgeSinkVProperty.ToScalarExpression(), vNodeIDProperty.ToScalarExpression());
                currentContext.AddPredicate(edgeToSinkVertexExp);

                currentContext.SetPivotVariable(inVertex);
            }
            else
            {
                GremlinEdgeToSinkVertexVariable inVertex = new GremlinEdgeToSinkVertexVariable(this);
                currentContext.VariableList.Add(inVertex);
                currentContext.TableReferencesInFromClause.Add(inVertex);
                currentContext.SetPivotVariable(inVertex);
            }
        }

        internal virtual void Is(GremlinToSqlContext currentContext, object value)
        {
            WScalarExpression firstExpr = DefaultProjection().ToScalarExpression();
            WScalarExpression secondExpr = SqlUtil.GetValueExpr(value);
            WBooleanExpression newPredicate = SqlUtil.GetEqualBooleanComparisonExpr(firstExpr, secondExpr);

            if (this.NeedFilter)
            {
                GremlinFilterVariable newVariable = new GremlinFilterVariable(newPredicate);
                currentContext.VariableList.Add(newVariable);
                currentContext.TableReferencesInFromClause.Add(newVariable);
            }
            else
            {
                currentContext.AddPredicate(newPredicate);
            }
        }

        internal virtual void Is(GremlinToSqlContext currentContext, Predicate predicate)
        {
            WScalarExpression firstExpr = DefaultProjection().ToScalarExpression();
            WScalarExpression secondExpr = SqlUtil.GetValueExpr(predicate.Value);
            WBooleanExpression newPredicate = SqlUtil.GetBooleanComparisonExpr(firstExpr, secondExpr, predicate);

            if (this.NeedFilter)
            {
                GremlinFilterVariable newVariable = new GremlinFilterVariable(newPredicate);
                currentContext.VariableList.Add(newVariable);
                currentContext.TableReferencesInFromClause.Add(newVariable);
            }
            else
            {
                currentContext.AddPredicate(newPredicate);
            }
        }

        internal virtual void Key(GremlinToSqlContext currentContext)
        {
            GremlinKeyVariable newVariable = new GremlinKeyVariable(this);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void Label(GremlinToSqlContext currentContext)
        {
            GremlinLabelVariable newVariable = new GremlinLabelVariable(this);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void Local(GremlinToSqlContext currentContext, GremlinToSqlContext localContext)
        {
            GremlinLocalVariable localMapVariable = new GremlinLocalVariable(localContext, localContext.PivotVariable.GetVariableType());
            currentContext.VariableList.Add(localMapVariable);
            currentContext.VariableList.AddRange(localContext.VariableList);

            currentContext.TableReferencesInFromClause.Add(localMapVariable);
            currentContext.SetPivotVariable(localMapVariable);
        }

        internal virtual void Map(GremlinToSqlContext currentContext, GremlinToSqlContext mapContext)
        {

            GremlinMapVariable mapVariable = new GremlinMapVariable(mapContext, mapContext.PivotVariable.GetVariableType());
            currentContext.VariableList.Add(mapVariable);
            currentContext.TableReferencesInFromClause.Add(mapVariable);
            currentContext.SetPivotVariable(mapVariable);
        }

        internal virtual void Max(GremlinToSqlContext currentContext)
        {
            GremlinMaxVariable newVariable = new GremlinMaxVariable(currentContext.Duplicate());
            currentContext.Reset();
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void MaxLocal(GremlinToSqlContext currentContext)
        {
            GremlinMaxLocalVariable newVariable = new GremlinMaxLocalVariable(this);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void Mean(GremlinToSqlContext currentContext)
        {
            GremlinMeanVariable newVariable = new GremlinMeanVariable(currentContext.Duplicate());
            currentContext.Reset();
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void MeanLocal(GremlinToSqlContext currentContext)
        {
            GremlinMeanLocalVariable newVariable = new GremlinMeanLocalVariable(this);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void Min(GremlinToSqlContext currentContext)
        {
            GremlinMinVariable newVariable = new GremlinMinVariable(currentContext.Duplicate());
            currentContext.Reset();
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void MinLocal(GremlinToSqlContext currentContext)
        {
            GremlinMinLocalVariable newVariable = new GremlinMinLocalVariable(this);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void Not(GremlinToSqlContext currentContext, GremlinToSqlContext notContext)
        {
            WBooleanExpression newPredicate = SqlUtil.GetNotExistPredicate(notContext.ToSelectQueryBlock());

            if (this.NeedFilter)
            {
                GremlinFilterVariable newVariable = new GremlinFilterVariable(newPredicate);
                currentContext.VariableList.Add(newVariable);
                currentContext.TableReferencesInFromClause.Add(newVariable);
            }
            else
            {
                currentContext.AddPredicate(newPredicate);
            }
        }

        internal virtual void Optional(GremlinToSqlContext currentContext, GremlinToSqlContext optionalContext)
        {
            GremlinVariableType variableType = GetVariableType() == optionalContext.PivotVariable.GetVariableType()
                ? GetVariableType()
                : GremlinVariableType.Table;
            GremlinOptionalVariable newVariable = new GremlinOptionalVariable(this, optionalContext, variableType);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void Or(GremlinToSqlContext currentContext, List<GremlinToSqlContext> orContexts)
        {
            List<WBooleanExpression> booleanExprList = new List<WBooleanExpression>();
            foreach (var context in orContexts)
            {
                currentContext.AllTableVariablesInWhereClause.AddRange(context.FetchAllTableVars());
                booleanExprList.Add(context.ToSqlBoolean());
            }
            WBooleanExpression newPredicate = SqlUtil.ConcatBooleanExprWithOr(booleanExprList);

            if (this.NeedFilter)
            {
                GremlinFilterVariable newVariable = new GremlinFilterVariable(newPredicate);
                currentContext.VariableList.Add(newVariable);
                currentContext.TableReferencesInFromClause.Add(newVariable);
            }
            else
            {
                currentContext.AddPredicate(newPredicate);
            }
        }

        internal virtual void Order(GremlinToSqlContext currentContext, List<Tuple<GremlinToSqlContext, IComparer>> byModulatingMap, GremlinKeyword.Scope scope)
        {
            GremlinOrderVariable newVariable = new GremlinOrderVariable(this, byModulatingMap, scope);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            if (scope == GremlinKeyword.Scope.Local)
            {
                currentContext.SetPivotVariable(newVariable);
            }
        }

        internal virtual void OtherV(GremlinToSqlContext currentContext)
        {
            GremlinEdgeToOtherVertexVariable otherSourceVertex = new GremlinEdgeToOtherVertexVariable(this);
            currentContext.VariableList.Add(otherSourceVertex);
            currentContext.TableReferencesInFromClause.Add(otherSourceVertex);
            currentContext.SetPivotVariable(otherSourceVertex);
        }

        internal virtual void Out(GremlinToSqlContext currentContext, List<string> edgeLabels)
        {
            GremlinVertexToForwardEdgeVariable outEdgeTable = new GremlinVertexToForwardEdgeVariable(this);
            currentContext.VariableList.Add(outEdgeTable);
            currentContext.TableReferencesInFromClause.Add(outEdgeTable);
            currentContext.AddLabelPredicateForEdge(outEdgeTable, edgeLabels);

            GremlinEdgeToSinkVertexVariable inSourceVertex = new GremlinEdgeToSinkVertexVariable(outEdgeTable);
            currentContext.VariableList.Add(inSourceVertex);
            currentContext.TableReferencesInFromClause.Add(inSourceVertex);

            currentContext.SetPivotVariable(inSourceVertex);
        }

        internal virtual void Out1(GremlinToSqlContext currentContext, List<string> edgeLabels)
        {
            if (this.GetVariableType() == GremlinVariableType.Vertex)
            {
                GremlinVertexToForwardEdgeVariable outEdgeTable = new GremlinVertexToForwardEdgeVariable(this);
                currentContext.VariableList.Add(outEdgeTable);
                currentContext.TableReferencesInFromClause.Add(outEdgeTable);
                currentContext.AddLabelPredicateForEdge(outEdgeTable, edgeLabels);

                GremlinFreeVertexVariable inSourceVertex = new GremlinFreeVertexVariable();
                currentContext.VariableList.Add(inSourceVertex);
                currentContext.TableReferencesInFromClause.Add(inSourceVertex);

                GremlinVariableProperty edgeSinkVProperty = outEdgeTable.GetVariableProperty(GremlinKeyword.EdgeSinkV);
                GremlinVariableProperty vNodeIDProperty = inSourceVertex.GetVariableProperty(GremlinKeyword.NodeID);
                WBooleanExpression edgeToSinkVertexExpr = SqlUtil.GetEqualBooleanComparisonExpr(edgeSinkVProperty.ToScalarExpression(), vNodeIDProperty.ToScalarExpression());
                currentContext.AddPredicate(edgeToSinkVertexExpr);

                currentContext.SetPivotVariable(inSourceVertex);
            }
            else
            {
                GremlinVertexToForwardEdgeVariable outEdgeTable = new GremlinVertexToForwardEdgeVariable(this);
                currentContext.VariableList.Add(outEdgeTable);
                currentContext.TableReferencesInFromClause.Add(outEdgeTable);
                currentContext.AddLabelPredicateForEdge(outEdgeTable, edgeLabels);

                GremlinEdgeToSinkVertexVariable inSourceVertex = new GremlinEdgeToSinkVertexVariable(outEdgeTable);
                currentContext.VariableList.Add(inSourceVertex);
                currentContext.TableReferencesInFromClause.Add(inSourceVertex);

                currentContext.SetPivotVariable(inSourceVertex);
            }

        }

        internal virtual void OutE(GremlinToSqlContext currentContext, List<string> edgeLabels)
        {
            GremlinVertexToForwardEdgeVariable outEdgeTable = new GremlinVertexToForwardEdgeVariable(this);
            currentContext.VariableList.Add(outEdgeTable);
            currentContext.TableReferencesInFromClause.Add(outEdgeTable);
            currentContext.AddLabelPredicateForEdge(outEdgeTable, edgeLabels);

            currentContext.SetPivotVariable(outEdgeTable);
        }

        internal virtual void OutV(GremlinToSqlContext currentContext)
        {
            GremlinEdgeToSourceVertexVariable outVertex = new GremlinEdgeToSourceVertexVariable(this);
            currentContext.VariableList.Add(outVertex);
            currentContext.TableReferencesInFromClause.Add(outVertex);
            currentContext.SetPivotVariable(outVertex);
        }

        internal virtual void OutV1(GremlinToSqlContext currentContext)
        {
            if (this.GetVariableType() == GremlinVariableType.Edge)
            {
                GremlinFreeVertexVariable outVertex = new GremlinFreeVertexVariable();
                currentContext.VariableList.Add(outVertex);
                currentContext.TableReferencesInFromClause.Add(outVertex);

                GremlinVariableProperty edgeSourceVProperty = this.GetVariableProperty(GremlinKeyword.EdgeSourceV);
                GremlinVariableProperty vNodeIDProperty = outVertex.GetVariableProperty(GremlinKeyword.NodeID);
                WBooleanExpression edgeToSinkVertexExpr = SqlUtil.GetEqualBooleanComparisonExpr(edgeSourceVProperty.ToScalarExpression(), vNodeIDProperty.ToScalarExpression());
                currentContext.AddPredicate(edgeToSinkVertexExpr);

                currentContext.SetPivotVariable(outVertex);
            }
            else
            {
                GremlinEdgeToSourceVertexVariable outVertex = new GremlinEdgeToSourceVertexVariable(this);
                currentContext.VariableList.Add(outVertex);
                currentContext.TableReferencesInFromClause.Add(outVertex);
                currentContext.SetPivotVariable(outVertex);
            }

        }

        private GremlinPathVariable GeneratePath(GremlinToSqlContext currentContext, List<GraphTraversal> byList, string fromLabel = null, string toLabel = null)
        {
            //TODO: refactor
            List<GremlinToSqlContext> byContexts = new List<GremlinToSqlContext>();
            List<GremlinVariable> steps = currentContext.GetGlobalPathStepList();
            if (byList == null)
            {
                byList = new List<GraphTraversal> { GraphTraversal.__() };
            }


            GremlinGlobalPathVariable newVariable = new GremlinGlobalPathVariable(steps, byContexts, fromLabel, toLabel);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);

            foreach (var by in byList)
            {
                GremlinToSqlContext newContext = new GremlinToSqlContext();
                GremlinDecompose1Variable decompose1 = new GremlinDecompose1Variable(newVariable);
                newContext.VariableList.Add(decompose1);
                newContext.TableReferencesInFromClause.Add(decompose1);
                newContext.SetPivotVariable(decompose1);

                by.GetStartOp().InheritedContextFromParent(newContext);
                byContexts.Add(by.GetEndOp().GetContext());
            }
            newVariable.ByContexts = byContexts;

            return newVariable;
        }

        private GremlinPathVariable GeneratePath(GremlinToSqlContext currentContext, string fromLabel = null, string toLabel = null)
        {
            //TODO: refactor
            List<GremlinToSqlContext> byContexts = new List<GremlinToSqlContext>();
            List<GremlinVariable> steps = currentContext.GetGlobalPathStepList();
            
            GremlinGlobalPathVariable newVariable = new GremlinGlobalPathVariable(steps, byContexts, fromLabel, toLabel);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            newVariable.ByContexts = byContexts;

            return newVariable;
        }

        internal virtual void Path(GremlinToSqlContext currentContext, List<GraphTraversal> byList, string fromLabel, string toLabel)
        {
            currentContext.SetPivotVariable(GeneratePath(currentContext, byList, fromLabel, toLabel));
        }

        internal virtual void Project(GremlinToSqlContext currentContext, List<string> projectKeys, List<GremlinToSqlContext> byContexts)
        {
            GremlinProjectVariable newVariable = new GremlinProjectVariable(projectKeys, byContexts);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void Properties(GremlinToSqlContext currentContext, List<string> propertyKeys)
        {
            GremlinPropertiesVariable newVariable = new GremlinPropertiesVariable(this, propertyKeys);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void Property(GremlinToSqlContext currentContext, GremlinProperty vertexProperty)
        {
            this.NeedFilter = true;
            GremlinUpdatePropertiesVariable updateVariable =
                currentContext.VariableList.Find(
                    p =>
                        (p is GremlinUpdatePropertiesVariable) &&
                        (p as GremlinUpdatePropertiesVariable).UpdateVariable == this) as GremlinUpdatePropertiesVariable;
            if (updateVariable == null)
            {
                updateVariable = new GremlinUpdatePropertiesVariable(this, vertexProperty);
                currentContext.VariableList.Add(updateVariable);
                currentContext.TableReferencesInFromClause.Add(updateVariable);
            }
            else
            {
                updateVariable.PropertyList.Add(vertexProperty);
            }
        }

        internal virtual void PropertyMap(GremlinToSqlContext currentContext, List<string> propertyKeys)
        {
            GremlinPropertyMapVariable newVariable = new GremlinPropertyMapVariable(this, propertyKeys);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void Range(GremlinToSqlContext currentContext, int low, int high, GremlinKeyword.Scope scope, bool isReverse)
        {
            GremlinRangeVariable newVariable = new GremlinRangeVariable(this, low, high, scope, isReverse);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            if (scope == GremlinKeyword.Scope.Local)
            {
                currentContext.SetPivotVariable(newVariable);
            }
        }

        internal virtual void Repeat(GremlinToSqlContext currentContext, GremlinToSqlContext repeatContext,
                                     RepeatCondition repeatCondition)
        {
            GremlinVariableType variableType = repeatContext.PivotVariable.GetVariableType() == GetVariableType()
                ? GetVariableType()
                : GremlinVariableType.Table;

            GremlinRepeatVariable repeatVariable = new GremlinRepeatVariable(this, repeatContext, repeatCondition, variableType);
            currentContext.VariableList.Add(repeatVariable);
            currentContext.TableReferencesInFromClause.Add(repeatVariable);
            currentContext.SetPivotVariable(repeatVariable);
            
            // TODO: refactor
            List<GremlinTableVariable> allTableVars = repeatVariable.FetchAllTableVars();
            foreach (var variable in allTableVars)
            {
                var pathVariable = variable as GremlinGlobalPathVariable;
                if (pathVariable != null)
                {
                    repeatVariable.PopulateLocalPath();
                    foreach (var property in pathVariable.ProjectedProperties)
                    {
                        repeatContext.ContextLocalPath.Populate(property, null);
                    }
                }
            }
        }

        internal virtual void Sample(GremlinToSqlContext currentContext, GremlinKeyword.Scope scope, int amountToSample, GremlinToSqlContext probabilityContext)
        {
            GremlinSampleVariable newVariable = new GremlinSampleVariable(scope, amountToSample, probabilityContext);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            //TODO: set pivotVariable when scope is local, need to sync with compilation and physical operator
        }

        internal virtual void SelectColumn(GremlinToSqlContext currentContext, GremlinKeyword.Column column)
        {
            GremlinSelectColumnVariable newVariable = new GremlinSelectColumnVariable(this, column);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual GremlinSelectVariable GenerateSelectVariable(GremlinToSqlContext currentContext, GremlinKeyword.Pop pop, List<string> selectKeys, List<GraphTraversal> byList=null)
        {
            //TODO: refactor
            if (byList == null)
            {
                byList = new List<GraphTraversal>() {GraphTraversal.__()};
            }
            List<GremlinToSqlContext> byContexts = new List<GremlinToSqlContext>();
            List<GremlinVariable> steps = currentContext.GetGlobalPathStepList();
            List<GremlinVariable> sideEffectVariables = currentContext.GetSideEffectVariables();

            GremlinGlobalPathVariable pathVariable = new GremlinGlobalPathVariable(steps);
            currentContext.VariableList.Add(pathVariable);
            currentContext.TableReferencesInFromClause.Add(pathVariable);

            foreach (var by in byList)
            {
                GremlinToSqlContext newContext = new GremlinToSqlContext();
                GremlinDecompose1Variable decompose1 = new GremlinDecompose1Variable(pathVariable);
                newContext.VariableList.Add(decompose1);
                newContext.TableReferencesInFromClause.Add(decompose1);
                newContext.SetPivotVariable(decompose1);

                by.GetStartOp().InheritedContextFromParent(newContext);
                byContexts.Add(by.GetEndOp().GetContext());
            }

            GremlinSelectVariable newVariable = new GremlinSelectVariable(this, pathVariable, sideEffectVariables, pop, selectKeys, byContexts);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);

            return newVariable;
        }

        internal virtual void Select(GremlinToSqlContext currentContext, GremlinKeyword.Pop pop, List<string> selectKeys, List<GraphTraversal> byList)
        {
            GremlinSelectVariable newVariable = GenerateSelectVariable(currentContext, pop, selectKeys, byList);
            currentContext.SetPivotVariable(newVariable);
        }
        
        internal virtual void SideEffect(GremlinToSqlContext currentContext, GremlinToSqlContext sideEffectContext)
        {
            this.NeedFilter = true;
            GremlinSideEffectVariable newVariable = new GremlinSideEffectVariable(sideEffectContext);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
        }

        internal virtual void SimplePath(GremlinToSqlContext currentContext, string fromLabel, string toLabel)
        {
            GremlinSimplePathVariable newVariable = new GremlinSimplePathVariable(GeneratePath(currentContext, fromLabel, toLabel));
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
        }

        internal virtual void Store(GremlinToSqlContext currentContext, string sideEffectKey, GremlinToSqlContext projectContext)
        {
            this.NeedFilter = true;
            GremlinStoreVariable newVariable = new GremlinStoreVariable(projectContext, sideEffectKey);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
        }

        internal virtual void Subgraph(GremlinToSqlContext currentContext, string sideEffectKey, GremlinToSqlContext dummyContext)
        {
            this.NeedFilter = true;
            GremlinSubgraphVariable newVariable = new GremlinSubgraphVariable(dummyContext, sideEffectKey);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
        }

        internal virtual void Sum(GremlinToSqlContext currentContext)
        {
            GremlinSumVariable newVariable = new GremlinSumVariable(currentContext.Duplicate());
            currentContext.Reset();
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void SumLocal(GremlinToSqlContext currentContext)
        {
            GremlinSumLocalVariable newVariable = new GremlinSumLocalVariable(this);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void TimeLimit(GremlinToSqlContext currentContext, long timeLimit)
        {
            throw new NotImplementedException();
        }

        internal virtual void Tree(GremlinToSqlContext currentContext, List<GraphTraversal> byList)
        {
            this.NeedFilter = true;
            GremlinPathVariable pathVariable = GeneratePath(currentContext, byList);
            GremlinTreeVariable newVariable = new GremlinTreeVariable(currentContext.Duplicate(), pathVariable);
            currentContext.Reset();
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void Tree(GremlinToSqlContext currentContext, string sideEffectKey, List<GraphTraversal> byList)
        {
            this.NeedFilter = true;
            GremlinPathVariable pathVariable = GeneratePath(currentContext, byList);
            GremlinTreeSideEffectVariable newVariable = new GremlinTreeSideEffectVariable(sideEffectKey, pathVariable);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
        }

        internal virtual void Unfold(GremlinToSqlContext currentContext)
        {
            GremlinUnfoldVariable newVariable = new GremlinUnfoldVariable(this);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void Union(ref GremlinToSqlContext currentContext, List<GremlinToSqlContext> unionContexts)
        {
            GremlinUnionVariable newVariable = new GremlinUnionVariable(unionContexts, GremlinUtil.GetContextListType(unionContexts));
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void Value(GremlinToSqlContext currentContext)
        {
            GremlinValueVariable newVariable = new GremlinValueVariable(this);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void ValueMap(GremlinToSqlContext currentContext, bool isIncludeTokens, List<string> propertyKeys)
        {
            GremlinValueMapVariable newVariable = new GremlinValueMapVariable(this, isIncludeTokens, propertyKeys);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void Values(GremlinToSqlContext currentContext, List<string> propertyKeys)
        {
            foreach (var property in propertyKeys)
            {
                Populate(property, null);
            }
            GremlinValuesVariable newVariable = new GremlinValuesVariable(this, propertyKeys);
            currentContext.VariableList.Add(newVariable);
            currentContext.TableReferencesInFromClause.Add(newVariable);
            currentContext.SetPivotVariable(newVariable);
        }

        internal virtual void Where(GremlinToSqlContext currentContext, Predicate predicate, TraversalRing traversalRing)
        {
            WBooleanExpression newPredicate = GetWherePredicate(currentContext, this, predicate, traversalRing);

            if (this.NeedFilter)
            {
                GremlinFilterVariable newVariable = new GremlinFilterVariable(newPredicate);
                currentContext.VariableList.Add(newVariable);
                currentContext.TableReferencesInFromClause.Add(newVariable);
            }
            else
            {
                currentContext.AddPredicate(newPredicate);
            }
        }

        internal virtual void Where(GremlinToSqlContext currentContext, string startKey, Predicate predicate, TraversalRing traversalRing)
        {
            var selectKey = new List<string> { startKey };
            var selectTraversal = new List<GraphTraversal> { traversalRing.Next() };
            var firstVar = GenerateSelectVariable(currentContext, GremlinKeyword.Pop.Last, selectKey, selectTraversal);
            WBooleanExpression newPredicate = GetWherePredicate(currentContext, firstVar, predicate, traversalRing);

            if (this.NeedFilter)
            {
                GremlinFilterVariable newVariable = new GremlinFilterVariable(newPredicate);
                currentContext.VariableList.Add(newVariable);
                currentContext.TableReferencesInFromClause.Add(newVariable);
            }
            else
            {
                currentContext.AddPredicate(newPredicate);
            }
        }

        internal virtual void Where(GremlinToSqlContext currentContext, GremlinToSqlContext whereContext)
        {
            currentContext.AllTableVariablesInWhereClause.AddRange(whereContext.FetchAllTableVars());
            WBooleanExpression newPredicate = whereContext.ToSqlBoolean();

            if (this.NeedFilter)
            {
                GremlinFilterVariable newVariable = new GremlinFilterVariable(newPredicate);
                currentContext.VariableList.Add(newVariable);
                currentContext.TableReferencesInFromClause.Add(newVariable);
            }
            else
            {
                currentContext.AddPredicate(newPredicate);
            }
        }
    }
}