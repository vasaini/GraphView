using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GraphView
{
    internal class GremlinDecompose1Variable: GremlinTableVariable
    {
        public GremlinVariable ComposeVariable { get; set; }

        public GremlinDecompose1Variable(GremlinVariable composeVariable) : base(GremlinVariableType.Table)
        {
            this.ComposeVariable = composeVariable;
        }

        internal override bool Populate(string property, string label = null)
        {
            if (this.ComposeVariable is GremlinPathVariable && property != GremlinKeyword.TableDefaultColumnName)
            {
                this.ComposeVariable.PopulateStepProperty(property, label);
                base.Populate(property, null);
            }
            else
            {
                this.ComposeVariable.Populate(property, label);
                base.Populate(property, null);
            }
            return true;
        }

        public override WTableReference ToTableReference()
        {
            List<WScalarExpression> parameters = new List<WScalarExpression>();
            parameters.Add(SqlUtil.GetColumnReferenceExpr(GremlinKeyword.Compose1TableDefaultName, GremlinKeyword.TableDefaultColumnName));
            parameters.Add(SqlUtil.GetValueExpr(this.DefaultProperty()));
            parameters.AddRange(this.ProjectedProperties.Select(SqlUtil.GetValueExpr));

            var tableRef = SqlUtil.GetFunctionTableReference(GremlinKeyword.func.Decompose1, parameters, GetVariableName());
            return SqlUtil.GetCrossApplyTableReference(tableRef);
        }
    }
}
