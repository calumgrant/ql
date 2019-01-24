/**
 * Modeling portals in C#.
 * 
 * A portal is a context-free dataflow summary, describing how data flows through third-party code,
 * and how portals can be sources and sinks of tainted data.
 */
import csharp
import dotnet

newtype TPortal = 
  TSummary(Summary c)
  or
  TCallReturn(Portal p) { exists(Summary call | call.hasReturn(p)) }
  or
  // Use param = -1 to denote qualifier
  TParameterPortal(Portal portal, int param) { exists(Summary call | call.hasParameter(portal, param)) }
  //or
  //TMethodCall(Portal qualifier, DotNet::Callable target) { exists(Summary call | call.hasMethodCall

class Portal extends TPortal {
  abstract string toString();
  
  predicate hasSource(Taint t) { exists(Summary call | call.hasSource(this, t)) }
  predicate hasSink(Taint t) { exists(Summary call | call.hasSink(this, t)) }

  ParameterPortal getParameter(int n) { result = TParameterPortal(this, n) }
  CallReturnPortal getReturn() { result = TCallReturn(this) }
  Location getLocation() { none() }
  
}

class SummaryPortal extends Portal, TSummary {
  Summary getSummary() { this = TSummary(result) }
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

abstract class Taint extends string {
  bindingset[this] Taint() { any() }
}

class Untainted extends Taint {
  Untainted() { this="" }
}

abstract class Summary extends DotNet::Callable {
  abstract predicate hasParameter(Portal portal, int param);
  abstract predicate hasReturn(Portal p);

  abstract predicate hasFlow(Portal src, Portal sink, Taint taint);
  
  predicate hasSource(Portal p, Taint t) { none() }
  predicate hasSink(Portal p, Taint t) { none() }

  final Portal getPortal() { result = TSummary(this) }
}

class CSharpParamReturn extends Summary, Callable {
  Parameter source;
  
  CSharpParamReturn() {
    this.canReturn(source.getAnAccess())  // !! Use dataflow
  }
  
  override predicate hasParameter(Portal portal, int param) {
    portal = getPortal() and source = this.getParameter(param)
  }

  override predicate hasReturn(Portal portal) { portal = getPortal() }
  
  override predicate hasFlow(Portal src, Portal sink, Taint taint) {
    taint = any(Untainted t)
    and
    src = getPortal().getParameter(source.getIndex())
    and
    sink = getPortal().getReturn()
  }
}

class CSharpThisReturn extends Summary, Callable {
  CSharpThisReturn() {
    this.canReturn(any(ThisAccess access))
  }
  
  override predicate hasReturn(Portal portal) { portal = getPortal() }

  override predicate hasParameter(Portal portal, int param) {
    portal = getPortal() and param = -1
  }

  override predicate hasFlow(Portal src, Portal sink, Taint taint) {
    taint = any(Untainted t)
    and
    src = getPortal().getParameter(-1)
    and
    sink = getPortal().getReturn()
  }
}

from Portal p
select p
