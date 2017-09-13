using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GraphView
{
    internal class GremlinChooseVariable : GremlinTableVariable
    {
        public GremlinToSqlContext ProjectContext { get; set; }
        public GremlinToSqlContext TrueChoiceContext { get; set; }
        public GremlinToSqlContext FalseChocieContext { get; set; }
        public GremlinToSqlContext ChoiceContext { get; set; }
        public Dictionary<object, GremlinToSqlContext> Options { get; set; }

        public GremlinChooseVariable(GremlinToSqlContext predicateContext, GremlinToSqlContext trueChoiceContext, GremlinToSqlContext falseChocieContext)
            : base(GremlinVariableType.Table)
        {
            this.ProjectContext = predicateContext;
            this.TrueChoiceContext = trueChoiceContext;
            this.FalseChocieContext = falseChocieContext;
            this.Options = new Dictionary<object, GremlinToSqlContext>();
        }

        public GremlinChooseVariable(GremlinToSqlContext choiceContext, Dictionary<object, GremlinToSqlContext> options)
            : base(GremlinVariableType.Table)
        {
            this.ChoiceContext = choiceContext;
            this.Options = options;
        }

        internal override bool Populate(string property, string label = null)
        {
            bool populateSuccess = false;
            if (base.Populate(property, label))
            {
                this.TrueChoiceContext?.Populate(property, null);
                this.FalseChocieContext?.Populate(property, null);
                foreach (var option in this.Options)
                {
                    option.Value.Populate(property, null);
                }
                populateSuccess = true;
            }
            else if (this.ProjectContext != null)
            {
                if (this.TrueChoiceContext.Populate(property, label))
                {
                    this.FalseChocieContext.Populate(property, null);
                    populateSuccess = base.Populate(property, null);
                }
                else if (this.FalseChocieContext.Populate(property, label))
                {
                    this.TrueChoiceContext.Populate(property, null);
                    populateSuccess = base.Populate(property, null);
                }
            }
            else
            {
                foreach (var option in this.Options)
                {
                    populateSuccess |= option.Value.Populate(property, label);
                }
                if (populateSuccess)
                {
                    base.Populate(property, null);
                }
            }
            return populateSuccess;
        }

        internal override List<GremlinVariable> FetchAllVars()
        {
            List<GremlinVariable> variableList = new List<GremlinVariable>() { this };
            if (this.ProjectContext != null)
            {
                variableList.AddRange(this.ProjectContext.FetchAllVars());
                variableList.AddRange(this.TrueChoiceContext.FetchAllVars());
                variableList.AddRange(this.FalseChocieContext.FetchAllVars());
            }
            else
            {
                variableList.AddRange(this.ChoiceContext.FetchAllVars());
                foreach (var option in this.Options)
                {
                    variableList.AddRange(option.Value.FetchAllVars());
                }
            }
            return variableList;
        }

        internal override List<GremlinTableVariable> FetchAllTableVars()
        {
            List<GremlinTableVariable> variableList = new List<GremlinTableVariable> { this };
            if (this.ProjectContext != null)
            {
                variableList.AddRange(this.ProjectContext.FetchAllTableVars());
                variableList.AddRange(this.TrueChoiceContext.FetchAllTableVars());
                variableList.AddRange(this.FalseChocieContext.FetchAllTableVars());
            }
            else
            {
                variableList.AddRange(this.ChoiceContext.FetchAllTableVars());
                foreach (var option in this.Options)
                {
                    variableList.AddRange(option.Value.FetchAllTableVars());
                }
            }
            return variableList;
        }

        internal override bool PopulateStepProperty(string property, string label)
        {
            bool populateSuccess = false;
            if (base.Populate(property, label))
            {
                this.TrueChoiceContext?.ContextLocalPath.PopulateStepProperty(property, null);
                this.FalseChocieContext?.ContextLocalPath.PopulateStepProperty(property, null);
                foreach (var option in this.Options)
                {
                    option.Value.ContextLocalPath.PopulateStepProperty(property, null);
                }
                populateSuccess = true;
            }
            else if (this.ProjectContext != null)
            {
                populateSuccess |= this.TrueChoiceContext.ContextLocalPath.PopulateStepProperty(property, label);
                populateSuccess |= this.FalseChocieContext.ContextLocalPath.PopulateStepProperty(property, label);
            }
            else
            {
                foreach (var option in this.Options)
                {
                    populateSuccess |= option.Value.ContextLocalPath.PopulateStepProperty(property, label);
                }
            }
            return populateSuccess;
        }

        internal override void PopulateLocalPath()
        {
            if (ProjectedProperties.Contains(GremlinKeyword.Path))
            {
                return;
            }
            ProjectedProperties.Add(GremlinKeyword.Path);
            if (this.ProjectContext != null)
            {
                this.TrueChoiceContext.PopulateLocalPath();
                this.FalseChocieContext.PopulateLocalPath();
            }
            else
            {
                foreach (var option in this.Options)
                {
                    option.Value.PopulateLocalPath();
                }
            }
        }

        internal override WScalarExpression ToStepScalarExpr()
        {
            return SqlUtil.GetColumnReferenceExpr(GetVariableName(), GremlinKeyword.Path);
        }

        public override WTableReference ToTableReference()
        {
            List<WScalarExpression> parameters = new List<WScalarExpression>();
            WTableReference tableReference;

            if (this.ProjectContext != null)
            {
                parameters.Add(SqlUtil.GetScalarSubquery(this.ProjectContext.ToSelectQueryBlock()));
                parameters.Add(SqlUtil.GetScalarSubquery(this.TrueChoiceContext.ToSelectQueryBlock()));
                parameters.Add(SqlUtil.GetScalarSubquery(this.FalseChocieContext.ToSelectQueryBlock()));
                tableReference = SqlUtil.GetFunctionTableReference(GremlinKeyword.func.Choose, parameters, GetVariableName());
            }
            else
            {
                parameters.Add(SqlUtil.GetScalarSubquery(this.ChoiceContext.ToSelectQueryBlock()));
                foreach (var option in this.Options)
                {
                    if (option.Key is GremlinKeyword.Pick && (GremlinKeyword.Pick) option.Key == GremlinKeyword.Pick.None)
                    {
                        parameters.Add(SqlUtil.GetValueExpr(null));
                    }
                    else
                    {
                        parameters.Add(SqlUtil.GetValueExpr(option.Key));
                    }
                    parameters.Add(SqlUtil.GetScalarSubquery(option.Value.ToSelectQueryBlock()));
                }
                tableReference = SqlUtil.GetFunctionTableReference(GremlinKeyword.func.ChooseWithOptions, parameters, GetVariableName());
            }
            return SqlUtil.GetCrossApplyTableReference(tableReference);
        }
    }
}
