﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GraphView
{
    internal class GremlinSelectVariable : GremlinTableVariable
    {
        public GremlinContextVariable InputVariable { get; set; }
        public GremlinPathVariable PathVariable { get; set; }
        public List<GremlinVariable> SideEffectVariables { get; set; } // Such as aggregate("a")/store("a")..
        public List<GremlinToSqlContext> ByContexts { get; set; }
        public List<string> SelectKeys { get; set; }
        public GremlinKeyword.Pop Pop { get; set; }

        public GremlinSelectVariable(GremlinVariable inputVariable, 
                                    GremlinPathVariable pathVariable, 
                                    List<GremlinVariable> sideEffectVariables, 
                                    GremlinKeyword.Pop pop, 
                                    List<string> selectKeys, 
                                    List<GremlinToSqlContext> byContexts)
            : base(GremlinVariableType.Table)
        {
            this.InputVariable = new GremlinContextVariable(inputVariable);
            this.PathVariable = pathVariable;
            this.SideEffectVariables = sideEffectVariables;
            this.Pop = pop;
            this.SelectKeys = selectKeys;
            this.ByContexts = byContexts;
        }

        internal override List<GremlinVariable> FetchAllVars()
        {
            List<GremlinVariable> variableList = new List<GremlinVariable>() { this };
            variableList.AddRange(this.PathVariable.FetchAllVars());
            foreach (var sideEffectVariable in this.SideEffectVariables)
            {
                variableList.AddRange(sideEffectVariable.FetchAllVars());
            }
            foreach (var context in this.ByContexts)
            {
                variableList.AddRange(context.FetchAllVars());
            }
            return variableList;
        }

        internal override List<GremlinTableVariable> FetchAllTableVars()
        {
            List<GremlinTableVariable> variableList = new List<GremlinTableVariable> { this };
            foreach (var context in this.ByContexts)
            {
                variableList.AddRange(context.FetchAllTableVars());
            }
            return variableList;
        }

        internal override bool Populate(string property, string label = null)
        {
            if (label == null || this.Labels.Contains(label))
            {
                foreach (string selectKey in this.SelectKeys)
                {
                    this.InputVariable.Populate(property, selectKey);
                    this.PathVariable.PopulateStepProperty(property, selectKey);
                    foreach (var sideEffectVariable in this.SideEffectVariables)
                    {
                        sideEffectVariable.Populate(property, selectKey);
                    }
                    foreach (var context in this.ByContexts)
                    {
                        context.Populate(property, selectKey);
                    }
                }
                if (SelectKeys.Count() == 1)
                {
                    return base.Populate(property);
                }
            }
            return false;
        }

        public override WTableReference ToTableReference()
        {
            List<WScalarExpression> parameters = new List<WScalarExpression>();
            List<WSelectQueryBlock> queryBlocks = new List<WSelectQueryBlock>();

            //Must toSelectQueryBlock before toCompose1 of variableList in order to populate needed columns
            //If only one selectKey, we just need to select the last one because it is a map flow.
            if (this.SelectKeys.Count == 1)
            {
                queryBlocks.Add(this.ByContexts[this.ByContexts.Count - 1].ToSelectQueryBlock(true));
            }
            else
            {
                queryBlocks.AddRange(this.ByContexts.Select(byContext => byContext.ToSelectQueryBlock(true)));
            }
            
            parameters.Add(this.InputVariable.DefaultProjection().ToScalarExpression());
            parameters.Add(this.PathVariable.DefaultProjection().ToScalarExpression());
            switch (this.Pop)
            {
                case GremlinKeyword.Pop.All:
                    parameters.Add(SqlUtil.GetValueExpr("All"));
                    break;
                case GremlinKeyword.Pop.First:
                    parameters.Add(SqlUtil.GetValueExpr("First"));
                    break;
                case GremlinKeyword.Pop.Last:
                    parameters.Add(SqlUtil.GetValueExpr("Last"));
                    break;
            }

            foreach (var selectKey in this.SelectKeys)
            {
                parameters.Add(SqlUtil.GetValueExpr(selectKey));
            }

            foreach (var block in queryBlocks)
            {
                parameters.Add(SqlUtil.GetScalarSubquery(block));
            }

            if (this.SelectKeys.Count == 1)
            {
                parameters.Add(SqlUtil.GetValueExpr(this.DefaultProperty()));
                foreach (var projectProperty in ProjectedProperties)
                {
                    parameters.Add(SqlUtil.GetValueExpr(projectProperty));
                }
            }

            var tableRef = SqlUtil.GetFunctionTableReference(
                                this.SelectKeys.Count == 1 ? GremlinKeyword.func.SelectOne: GremlinKeyword.func.Select, 
                                parameters, GetVariableName());
            return SqlUtil.GetCrossApplyTableReference(tableRef);
        }
    }
}
