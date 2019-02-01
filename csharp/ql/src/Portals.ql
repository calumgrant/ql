/**
 * Modeling portals in C#.
 * 
 * A portal is a context-free dataflow description, describing how data flows through third-party code,
 * and how portals can be sources and sinks of tainted data.
 */
import csharp
import dotnet
import cil

newtype TPortal = 
  TCallablePortal(CallableFlowSummary c)
  or
  TCallReturn(Portal p) { exists(CallableFlowSummary call | call.hasReturn(p)) }
  or
  TParameterPortal(Portal portal, int param) { exists(CallableFlowSummary call | call.hasParameter(portal, param)) }
  or
  TQualifierPortal(Portal portal) { exists(CallableFlowSummary call | call.hasQualifier(portal)) }
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
  Portal getCall() { result = TCallReturn(this) }
  override string toString() { result = "return of " + getCall() }
  override Location getLocation() { result = getCall().getLocation() }
}

class ParameterPortal extends Portal, TParameterPortal {
  Portal getCall() { this = TParameterPortal(result,_) }
  int getParameter() { this = TParameterPortal(_,result) }
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
  bindingset[this] Taint() { any() }
  abstract string getComposition(Taint other);
}

class Untainted extends Taint {
  Untainted() { this="" }
  string getComposition(Taint other) { result = other }
}

class Tainted extends Taint {
  Tainted() { this="tainted" }
  string getComposition(Taint other) { result = this }
}

/**
 * 
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

class CSharpParamReturn extends CallableFlowSummary, Callable {
  Parameter param;
  Taint taint;
  
  CSharpParamReturn() {
    exists(DataFlow::ParameterNode source, DataFlow::ExprNode sink
    | source.asParameter() = param |
      DataFlow::localFlow(source, sink) and
      this.canReturn(sink.asExpr()) and
      taint instanceof Untainted
    )
    // ?? Taint tracking?
    
    
//    asExpr() = source.getAnAccess() and 
//    this.canReturn(source.getAnAccess())  // !! Use dataflow
  }
  
  override predicate hasParameter(Portal portal, int p) {
    portal = getPortal() and param = this.getParameter(p)
  }

  override predicate hasQualifier(Portal p) { none() }

  override predicate hasReturn(Portal portal) { portal = getPortal() }
  
  override predicate hasFlow(Portal src, Portal sink, Taint t) {
    t = taint
    and
    src = getPortal().getParameter(param.getIndex())
    and
    sink = getPortal().getReturn()
  }
}

class CSharpThisReturn extends CallableFlowSummary, Callable {
  CSharpThisReturn() {
    this.canReturn(any(ThisAccess access))
  }
  
  override predicate hasQualifier(Portal p) { p=getPortal() }
  
  override predicate hasReturn(Portal portal) { portal = getPortal() }

  override predicate hasParameter(Portal portal, int param) { none() }

  override predicate hasFlow(Portal src, Portal sink, Taint taint) {
    taint = any(Untainted t)
    and
    src = getPortal().getQualifier()
    and
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
    taint = any(Untainted t)
    and
    src = getPortal().getParameter(source.getIndex())
    and
    sink = getPortal().getReturn()
  }
}

from Portal p
select p
