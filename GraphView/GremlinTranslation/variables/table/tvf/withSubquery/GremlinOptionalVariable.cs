using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GraphView
{
    internal class GremlinOptionalVariable : GremlinTableVariable
    {
        public GremlinToSqlContext OptionalContext { get; set; }
        public GremlinContextVariable InputVariable { get; set; }

        public GremlinOptionalVariable(GremlinVariable inputVariable,
                                       GremlinToSqlContext context,
                                       GremlinVariableType variableType)
            : base(variableType)
        {
            inputVariable.ProjectedProperties.Clear();
            this.OptionalContext = context;
            this.InputVariable = new GremlinContextVariable(inputVariable);
        }

        internal override bool PopulateStepProperty(string property, string label = null)
        {
            return this.OptionalContext.ContextLocalPath.PopulateStepProperty(property, label);
        }

        internal override void PopulateLocalPath()
        {
            if (ProjectedProperties.Contains(GremlinKeyword.Path))
            {
                return;
            }
            ProjectedProperties.Add(GremlinKeyword.Path);
            this.OptionalContext.PopulateLocalPath();
        }

        internal override bool Populate(string property, string label = null)
        {
            if (base.Populate(property, label))
            {
                this.InputVariable.Populate(property, null);
                this.OptionalContext.Populate(property, null);
                return true;
            }
            else
            {
                bool populateSuccess = false;
                populateSuccess |= this.InputVariable.Populate(property, label);
                populateSuccess |= this.OptionalContext.Populate(property, label);
                if (populateSuccess)
                {
                    base.Populate(property, label);
                }
                return populateSuccess;
            }
        }

        internal override WScalarExpression ToStepScalarExpr()
        {
            return SqlUtil.GetColumnReferenceExpr(GetVariableName(), GremlinKeyword.Path);
        }

        internal override List<GremlinVariable> FetchAllVars()
        {
            List<GremlinVariable> variableList = new List<GremlinVariable>() { this };
            variableList.Add(this.InputVariable);
            variableList.AddRange(this.OptionalContext.FetchAllVars());
            return variableList;
        }

        internal override List<GremlinTableVariable> FetchAllTableVars()
        {
            List<GremlinTableVariable> variableList = new List<GremlinTableVariable> { this };
            variableList.AddRange(this.OptionalContext.FetchAllTableVars());
            return variableList;
        }

        public override WTableReference ToTableReference()
        {
            WSelectQueryBlock firstQueryExpr = new WSelectQueryBlock();

            foreach (var projectProperty in ProjectedProperties)
            {
                if (projectProperty == GremlinKeyword.TableDefaultColumnName)
                {
                    firstQueryExpr.SelectElements.Add(SqlUtil.GetSelectScalarExpr(this.InputVariable.DefaultProjection().ToScalarExpression(),
                        GremlinKeyword.TableDefaultColumnName));
                }
                else if (this.InputVariable.RealVariable.ProjectedProperties.Contains(projectProperty))
                {
                    firstQueryExpr.SelectElements.Add(
                        SqlUtil.GetSelectScalarExpr(
                            this.InputVariable.RealVariable.GetVariableProperty(projectProperty).ToScalarExpression(), projectProperty));
                }
                else
                {
                    firstQueryExpr.SelectElements.Add(
                        SqlUtil.GetSelectScalarExpr(SqlUtil.GetValueExpr(null), projectProperty));
                }
            }

            WSelectQueryBlock secondQueryExpr = this.OptionalContext.ToSelectQueryBlock();

            var WBinaryQueryExpression = SqlUtil.GetBinaryQueryExpr(firstQueryExpr, secondQueryExpr);

            List<WScalarExpression> parameters = new List<WScalarExpression>();
            parameters.Add(SqlUtil.GetScalarSubquery(WBinaryQueryExpression));
            var tableRef = SqlUtil.GetFunctionTableReference(GremlinKeyword.func.Optional, parameters, GetVariableName());

            return SqlUtil.GetCrossApplyTableReference(tableRef);
        }
    }
}
