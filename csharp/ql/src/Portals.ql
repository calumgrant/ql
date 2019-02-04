/**
 * Modeling portals in C#.
 *
 * A portal is a context-free dataflow description, describing how data flows through third-party code,
 * and how portals can be sources and sinks of tainted data.
 */

import csharp
import dotnet
import cil
import semmle.code.csharp.dataflow.TaintTracking

newtype TPortal =
  TCallablePortal(CallableFlowSummary c) or
  TCallReturn(CallablePortal p) { exists(CallableFlowSummary call | call.hasReturn(p)) } or
  TParameterPortal(CallablePortal portal, int param) {
    exists(CallableFlowSummary call | call.hasParameter(portal, param))
  } or
  TQualifierPortal(CallablePortal portal) {
    exists(CallableFlowSummary call | call.hasQualifier(portal))
  }

//or
//TMethodCall(Portal qualifier, DotNet::Callable target) { exists(Summary call | call.hasMethodCall
/**
 * A portal is an abstract dataflow node
 */
class Portal extends TPortal {
  abstract string toString();

  // Data that flows out of this portal
  predicate hasSource(Taint t) { exists(CallableFlowSummary call | call.hasSource(this, t)) }

  // Data that flows in to this portal
  predicate hasSink(Taint t) { exists(CallableFlowSummary call | call.hasSink(this, t)) }

  predicate flowsTo(Portal dest, Taint t) {
    exists(CallableFlowSummary call | call.hasFlow(this, dest, t))
  }

  ParameterPortal getParameter(int n) { result = TParameterPortal(this, n) }

  CallReturnPortal getReturn() { result = TCallReturn(this) }

  QualifierPortal getQualifier() { result = TQualifierPortal(this) }

  Location getLocation() { none() }
}

class CallablePortal extends Portal, TCallablePortal {
  CallableFlowSummary getSummary() { this = TCallablePortal(result) }

  override string toString() { result = getSummary().toString() }

  override Location getLocation() { result = getSummary().getLocation() }
}

class CallReturnPortal extends Portal, TCallReturn {
  CallablePortal getCall() { this = TCallReturn(result) }

  override string toString() { result = "return of " + getCall() }

  override Location getLocation() { result = getCall().getLocation() }
}

class ParameterPortal extends Portal, TParameterPortal {
  CallablePortal getCall() { this = TParameterPortal(result, _) }

  int getParameter() { this = TParameterPortal(_, result) }

  override string toString() { result = "parameter " + getParameter() + " of " + getCall() }

  override Location getLocation() { result = getCall().getLocation() }
}

class QualifierPortal extends Portal, TQualifierPortal {
  Portal getCall() { this = TQualifierPortal(result) }

  override string toString() { result = "qualifier to " + getCall() }

  override Location getLocation() { result = getCall().getLocation() }
}

/** A description of how data is tainted, OR, how taint is modified. */
abstract class Taint extends string {
  bindingset[this]
  Taint() { any() }

  abstract string getComposition(Taint other);
}

class Untainted extends Taint {
  Untainted() { this = "" }

  string getComposition(Taint other) { result = other }
}

class Tainted extends Taint {
  Tainted() { this = "tainted" }

  string getComposition(Taint other) { result = this }
}

/**
 */
abstract class CallableFlowSummary extends DotNet::Callable {
  abstract predicate hasParameter(Portal portal, int param);

  abstract predicate hasReturn(Portal p);

  abstract predicate hasQualifier(Portal p);

  abstract predicate hasFlow(Portal src, Portal sink, Taint taint);

  predicate hasSource(Portal p, Taint t) { none() }

  predicate hasSink(Portal p, Taint t) { none() }

  final CallablePortal getPortal() { result = TCallablePortal(this) }
}

private predicate callFlowThrough(DataFlow::Node n1, DataFlow::Node n2, Taint taintKind) {
  exists(CallableFlowSummary fs, Call c, ParameterPortal src, CallReturnPortal sink |
    fs.hasFlow(src, sink, taintKind)
  |
    fs = c.getTarget() and // !! Also a CIL callable ??
    n1.asExpr() = c.getArgument(src.getParameter()) and
    n2.asExpr() = c
  )
}

private predicate localFlowStep(DataFlow::Node n1, DataFlow::Node n2, Taint taintKind) {
  DataFlow::localFlowStep(n1, n2) and taintKind instanceof Untainted
  or
  TaintTracking::localAdditionalTaintStep2(n1, n2) and
  taintKind instanceof Tainted // and
  or
  // not DataFlow::localFlowStep(n1, n2)
  callFlowThrough(n1, n2, taintKind)
}

query predicate flowThrough(DataFlow::Node n1, DataFlow::Node n2, Taint taintKind) {
  callFlowThrough(n1, n2, taintKind)
}

private predicate localFlowStepPlus(DataFlow::Node n1, DataFlow::Node n2, Taint taintKind) {
  localFlowStep(n1, n2, taintKind)
  or
  exists(DataFlow::Node mid, Taint taint1, Taint taint2 |
    localFlowStep(n1, mid, taint1) and
    localFlowStepPlus(mid, n2, taint2) and
    taintKind = taint1.getComposition(taint2)
  )
}

predicate localFlow(DataFlow::Node n1, DataFlow::Node n2, Taint taintKind) {
  n1 = n2 and taintKind instanceof Untainted
  or
  localFlowStepPlus(n1, n2, taintKind)
}

private predicate csharpParamReturn(Callable callable, Parameter param, Taint taint) {
  exists(DataFlow::ParameterNode source, DataFlow::ExprNode sink | source.asParameter() = param |
    localFlow(source, sink, taint) and
    callable.canReturn(sink.asExpr())
  )
}

class CSharpParamReturn extends CallableFlowSummary, Callable {
  Parameter param;

  Taint taint;

  CSharpParamReturn() {
    csharpParamReturn(this, param, taint)
    /*
     *    exists(DataFlow::ParameterNode source, DataFlow::ExprNode sink
     *    | source.asParameter() = param |
     *      localFlow(source, sink, taint) and
     *      this.canReturn(sink.asExpr())
     *    )
     */

    //    asExpr() = source.getAnAccess() and
    //    this.canReturn(source.getAnAccess())  // !! Use dataflow
    }

  override predicate hasParameter(Portal portal, int p) {
    portal = getPortal() and param = this.getParameter(p)
  }

  override predicate hasQualifier(Portal p) { none() }

  override predicate hasReturn(Portal portal) { portal = getPortal() }

  override predicate hasFlow(Portal src, Portal sink, Taint t) {
    csharpParamReturn(this, param, taint) and
    t = taint and
    src = getPortal().getParameter(param.getIndex()) and
    sink = getPortal().getReturn()
  }
}

class CSharpThisReturn extends CallableFlowSummary, Callable {
  CSharpThisReturn() { this.canReturn(any(ThisAccess access)) }

  override predicate hasQualifier(Portal p) { p = getPortal() }

  override predicate hasReturn(Portal portal) { portal = getPortal() }

  override predicate hasParameter(Portal portal, int param) { none() }

  override predicate hasFlow(Portal src, Portal sink, Taint taint) {
    taint = any(Untainted t) and
    src = getPortal().getQualifier() and
    sink = getPortal().getReturn()
  }
}

class CilParamReturn extends CallableFlowSummary, CIL::Callable {
  CIL::Parameter source;

  CilParamReturn() {
    this.canReturn(source.getARead()) // !! Use dataflow
  }

  override predicate hasQualifier(Portal portal) {
    portal = getPortal() and
    this.canReturn(any(CIL::ThisAccess a))
  }

  override predicate hasReturn(Portal portal) { portal = getPortal() }

  override predicate hasParameter(Portal portal, int param) {
    portal = getPortal() and param = source.getIndex()
  }

  override predicate hasFlow(Portal src, Portal sink, Taint taint) {
    taint = any(Untainted t) and
    src = getPortal().getParameter(source.getIndex()) and
    sink = getPortal().getReturn()
  }
}

query predicate flows(Portal source, Portal sink, Taint taint) { source.flowsTo(sink, taint) }

query predicate allPortals(Portal p) { any() }

query predicate cilPortalFlow(Portal source, Portal sink, Taint taint) {
  exists(CilParamReturn ret | ret.hasFlow(source, sink, taint))
}
