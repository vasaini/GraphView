﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace GraphView
{
    internal class GremlinUnionVariable : GremlinTableVariable
    {
        public List<GremlinToSqlContext> UnionContextList { get; set; }

        public GremlinUnionVariable(List<GremlinToSqlContext> unionContextList, GremlinVariableType variableType)
            : base(variableType)
        {
            this.UnionContextList = unionContextList;
        }

        internal override bool Populate(string property, string label = null)
        {
            if (base.Populate(property, label))
            {
                foreach (var context in this.UnionContextList)
                {
                    context.Populate(property, null);
                }
                return true;
            }
            else
            {
                bool populateSuccess = false;
                foreach (var context in this.UnionContextList)
                {
                    populateSuccess |= context.Populate(property, label);
                }
                if (populateSuccess)
                {
                    base.Populate(property, null);
                }
                return populateSuccess;
            }
        }

        internal override bool PopulateStepProperty(string property, string label = null)
        {
            foreach (var context in this.UnionContextList)
            {
                context.ContextLocalPath.PopulateStepProperty(property, label);
            }
            return false;
        }

        internal override void PopulateLocalPath()
        {
            if (this.ProjectedProperties.Contains(GremlinKeyword.Path))
            {
                return;
            }
            this.ProjectedProperties.Add(GremlinKeyword.Path);
            foreach (var context in this.UnionContextList)
            {
                context.PopulateLocalPath();
            }
        }

        internal override WScalarExpression ToStepScalarExpr()
        {
            return SqlUtil.GetColumnReferenceExpr(GetVariableName(), GremlinKeyword.Path);
        }

        internal override List<GremlinVariable> FetchAllVars()
        {
            List<GremlinVariable> variableList = new List<GremlinVariable>() { this };
            foreach (var context in this.UnionContextList)
            {
                variableList.AddRange(context.FetchAllVars());
            }
            return variableList;
        }

        internal override List<GremlinTableVariable> FetchAllTableVars()
        {
            List<GremlinTableVariable> variableList = new List<GremlinTableVariable> { this };
            foreach (var context in this.UnionContextList)
            {
                variableList.AddRange(context.FetchAllTableVars());
            }
            return variableList;
        }

        public override WTableReference ToTableReference()
        {
            List<WScalarExpression> parameters = new List<WScalarExpression>();
            if (this.UnionContextList.Count == 0)
            {
                parameters.Add(SqlUtil.GetValueExpr(this.DefaultProperty()));
                parameters.AddRange(this.ProjectedProperties.Select(SqlUtil.GetValueExpr));
            }
            else
            {
                List<WSelectQueryBlock> selectQueryBlocks = new List<WSelectQueryBlock>();
                selectQueryBlocks.AddRange(this.UnionContextList.Select(context => context.ToSelectQueryBlock()));
                this.AlignSelectQueryBlocks(selectQueryBlocks);
                parameters.AddRange(selectQueryBlocks.Select(SqlUtil.GetScalarSubquery));
            }

            var tableRef = SqlUtil.GetFunctionTableReference(GremlinKeyword.func.Union, parameters, GetVariableName());
            return SqlUtil.GetCrossApplyTableReference(tableRef);
        }
    }
}
