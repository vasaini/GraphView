using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GraphView
{
    internal class GremlinCoalesceVariable : GremlinTableVariable
    {
        public List<GremlinToSqlContext> CoalesceContextList { get; set; }

        public GremlinCoalesceVariable(List<GremlinToSqlContext> coalesceContextList, GremlinVariableType variableType)
            : base(variableType)
        {
            this.CoalesceContextList = new List<GremlinToSqlContext>(coalesceContextList);
        }

        internal override bool Populate(string property, string label = null)
        {
            bool populateSuccess = false;
            foreach (var context in this.CoalesceContextList)
            {
                populateSuccess |= context.Populate(property, label);
            }
            if (populateSuccess)
            {
                base.Populate(property, null);
            }
            return populateSuccess;
        }

        internal override List<GremlinVariable> FetchAllVars()
        {
            List<GremlinVariable> variableList = new List<GremlinVariable>() {this};
            foreach (var context in this.CoalesceContextList)
            {
                variableList.AddRange(context.FetchAllVars());
            }
            return variableList;
        }

        internal override List<GremlinTableVariable> FetchAllTableVars()
        {
            List<GremlinTableVariable> variableList = new List<GremlinTableVariable> { this };
            foreach (var context in this.CoalesceContextList)
            {
                variableList.AddRange(context.FetchAllTableVars());
            }
            return variableList;
        }

        public override  WTableReference ToTableReference()
        {
            List<WScalarExpression> parameters = new List<WScalarExpression>();
            parameters.AddRange(
                this.CoalesceContextList.Select(context => SqlUtil.GetScalarSubquery(context.ToSelectQueryBlock())));

            var tableRef = SqlUtil.GetFunctionTableReference(GremlinKeyword.func.Coalesce, parameters, GetVariableName());
            return SqlUtil.GetCrossApplyTableReference(tableRef);
        }
    }
}
