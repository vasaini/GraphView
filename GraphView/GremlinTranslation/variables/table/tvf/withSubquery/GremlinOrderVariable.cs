using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GraphView
{
    internal class GremlinOrderVariable: GremlinTableVariable
    {
        public List<Tuple<GremlinToSqlContext, IComparer>> ByModulatingList;
        public GremlinKeyword.Scope Scope { get; set; }
        public GremlinContextVariable InputVariable { get; set; }
        public GremlinOrderVariable(GremlinVariable inputVariable, List<Tuple<GremlinToSqlContext, IComparer>> byModulatingList, GremlinKeyword.Scope scope)
            :base(GremlinVariableType.Table)
        {
            this.ByModulatingList = byModulatingList;
            this.Scope = scope;
            this.InputVariable = new GremlinContextVariable(inputVariable);
        }

        internal override List<GremlinVariable> FetchAllVars()
        {
            List<GremlinVariable> variableList = new List<GremlinVariable>() { this };
            variableList.Add(this.InputVariable);
            foreach (var by in this.ByModulatingList)
            {
                variableList.AddRange(by.Item1.FetchAllVars());
            }
            return variableList;
        }

        internal override List<GremlinTableVariable> FetchAllTableVars()
        {
            List<GremlinTableVariable> variableList = new List<GremlinTableVariable> { this };
            foreach (var by in this.ByModulatingList)
            {
                variableList.AddRange(by.Item1.FetchAllTableVars());
            }
            return variableList;
        }

        internal override bool Populate(string property, string label = null)
        {
            if (base.Populate(property, label))
            {
                return this.InputVariable.Populate(property, label);
            }
            else if (this.InputVariable.Populate(property, label))
            {
                return base.Populate(property, null);
            }
            else
            {
                return false;
            }
        }

        public override WTableReference ToTableReference()
        {
            List<WScalarExpression> parameters = new List<WScalarExpression>();

            var tableRef = this.Scope == GremlinKeyword.Scope.Global
              ? SqlUtil.GetFunctionTableReference(GremlinKeyword.func.OrderGlobal, parameters, GetVariableName())
              : SqlUtil.GetFunctionTableReference(GremlinKeyword.func.OrderLocal, parameters, GetVariableName());

            var wOrderTableReference = tableRef as WOrderTableReference;
            if (wOrderTableReference != null)
                wOrderTableReference.OrderParameters = new List<Tuple<WScalarExpression, IComparer>>();

            if (this.Scope == GremlinKeyword.Scope.Local)
            {
                ((WOrderLocalTableReference)tableRef).Parameters.Add(this.InputVariable.DefaultProjection().ToScalarExpression());
            }

            foreach (var pair in this.ByModulatingList)
            {
                WScalarExpression scalarExpr = SqlUtil.GetScalarSubquery(pair.Item1.ToSelectQueryBlock());

                var orderTableReference = tableRef as WOrderTableReference;
                orderTableReference?.OrderParameters.Add(new Tuple<WScalarExpression, IComparer>(scalarExpr, pair.Item2));
                orderTableReference?.Parameters.Add(scalarExpr);
            }

            if (this.Scope == GremlinKeyword.Scope.Local)
            {
                foreach (var property in ProjectedProperties)
                {
                    wOrderTableReference.Parameters.Add(SqlUtil.GetValueExpr(property));
                }
            }

            return SqlUtil.GetCrossApplyTableReference(tableRef);
        }
    }
}
