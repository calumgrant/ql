import csharp
private import cil

private newtype TPortal = 
  TReturnPortal()
  or
  TParameterPortal(int param) {
    exists(FlowSummary f | f.hasParameter(param))
  }
  or
  TThis()
  or
  TInvokeReturn(TPortal p) {
    exists(FlowSummary f | f.invokes(p, _))
  }
  or
  TInvokeParameter(TPortal p, int param)
  {
    exists(FlowSummary f | f.invokes(p, param))
  }

class Portal extends TPortal {
  abstract string toString();
}

class ReturnPortal extends Portal, TReturnPortal {
  override string toString() { result = "return" }
}

class ParameterPortal extends Portal, TParameterPortal {

  int getParameter() { this = TParameterPortal(result) }
  
  override string toString() { result = "(" + getParameter() + ")" }
}
  
abstract class FlowSummary extends Callable {
  
  predicate invokes(Portal p, int param) { none() }
  predicate hasSource(Portal p, string tag) { none() }
  predicate hasParameter(int param) { exists(this.getParameter(param)) }
  
  predicate hasSink(Portal p, string tag) { none() }
  
  predicate hasFlow(Portal p1, Portal p2, string taint) { none() }
}

class CilFlowSummary extends FlowSummary {
  CIL::Method method;
  
  CilFlowSummary() {
    this.matchesHandle(method)
  }
  
  CIL::Method getCilMethod() { result = method }
  
  override predicate hasFlow(Portal p1, Portal p2, string taint) {
    // Parameter-return flow
    exists(int param, CIL::ParameterReadAccess access | 
      method.canReturn(access) and
      p1.(ParameterPortal).getParameter() = param and
      p2 instanceof ReturnPortal and
      taint="" and
      access.getTarget().getIndex() = param
    )
    or
    // This-return flow
    exists(CIL::ThisAccess access |
      method.canReturn(access) |
      p1.(ParameterPortal).getParameter() = -1 and
      p2 instanceof ReturnPortal and
      taint=""
    )
    or
    // Indirect parameter-return flow
    exists(CIL::Call c, CIL::ParameterReadAccess access, int param,
      CilFlowSummary targetFlow, ParameterPortal p3, ReturnPortal p4, string taint2, CIL::Expr ret | 
      method.getParameter(param).flowsTo(access) and
      access = c.getArgument(p3.getParameter()) and
      method.canReturn(ret) and
      c.flowsTo(ret) and
      p1.(ParameterPortal).getParameter() = param and
      p2 instanceof ReturnPortal and
      targetFlow.getCilMethod() = c.getTarget() and
      targetFlow.hasFlow(p3, p4, taint2) and
      taint = taint2
    )
  }
}

