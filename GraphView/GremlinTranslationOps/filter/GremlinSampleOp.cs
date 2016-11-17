﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.SqlServer.TransactSql.ScriptDom;

namespace GraphView.GremlinTranslationOps.filter
{
    internal class GremlinSampleOp: GremlinTranslationOperator
    {
        public long AmountToSample;

        public GremlinSampleOp(long amountToSample)
        {
            AmountToSample = amountToSample;
        }

        public override GremlinToSqlContext GetContext()
        {
            GremlinToSqlContext inputContext = GetInputContext();

            WColumnReferenceExpression columnRefExpr =
                GremlinUtil.GetColumnReferenceExpression(inputContext.CurrVariable.VariableName);
            WValueExpression valueExpr = GremlinUtil.GetValueExpression(AmountToSample.ToString());
            WFunctionCall functionCall = GremlinUtil.GetFunctionCall("sample", columnRefExpr, valueExpr);

            WColumnReferenceExpression trueExpr = GremlinUtil.GetColumnReferenceExpression("true");

            WBooleanExpression booleanExpr = GremlinUtil.GetBooleanComparisonExpr(functionCall, trueExpr, BooleanComparisonType.Equals);

            inputContext.AddPredicate(booleanExpr);

            return inputContext;
        }
    }
}