using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GraphView
{
    internal class GremlinContextVariable: GremlinVariable
    {
        public GremlinVariable RealVariable { get; set; }

        internal override GremlinVariableType GetVariableType()
        {
            return this.RealVariable.GetVariableType();
        }

        internal override string GetVariableName()
        {
            return this.RealVariable.GetVariableName();
        }

        public GremlinContextVariable(GremlinVariable contextVariable)
        {
            this.RealVariable = contextVariable;
        }

        internal override GremlinVariableProperty GetVariableProperty(string property)
        {
            this.Populate(property, null);
            return this.RealVariable.GetVariableProperty(property);
        }

        internal override bool Populate(string property, string label = null)
        {
            if (base.Populate(property, label))
            {
                return this.RealVariable.Populate(property, null);
            }
            else if (this.RealVariable.Populate(property, label))
            {
                return base.Populate(property, null);
            }
            else
            {
                return false;
            }

            //switch (this.RealVariable.GetVariableType())
            //{
            //    case GremlinVariableType.Vertex:
            //        if (GremlinUtil.IsEdgeProperty(property)) return;
            //        break;
            //    case GremlinVariableType.Edge:
            //        if (GremlinUtil.IsVertexProperty(property)) return;
            //        break;
            //    case GremlinVariableType.VertexProperty:
            //        if (GremlinUtil.IsVertexProperty(property) || GremlinUtil.IsEdgeProperty(property)) return;
            //        break;
            //    case GremlinVariableType.Scalar:
            //    case GremlinVariableType.Property:
            //        if (property != GremlinKeyword.TableDefaultColumnName) return;
            //        break;
            //}
        }

        internal override bool PopulateStepProperty(string property, string label = null)
        {
            if (this.RealVariable.PopulateStepProperty(property, label))
            {
                return base.PopulateStepProperty(property, null);
            }
            else if (base.PopulateStepProperty(property, label))
            {
                return this.RealVariable.PopulateStepProperty(property, null);
            }
            else
            {
                return false;
            }
        }
    }

    internal class GremlinRepeatContextVariable : GremlinContextVariable
    {
        public GremlinRepeatContextVariable(GremlinVariable contextVariable) : base(contextVariable) {}
    }

    internal class GremlinUntilContextVariable : GremlinContextVariable
    {
        public GremlinUntilContextVariable(GremlinVariable contextVariable) : base(contextVariable) {}
    }

    internal class GremlinEmitContextVariable : GremlinContextVariable
    {
        public GremlinEmitContextVariable(GremlinVariable contextVariable) : base(contextVariable) {}
    }
}
