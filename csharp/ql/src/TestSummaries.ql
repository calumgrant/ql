import csharp

import semmle.code.csharp.dataflow.Summaries


from FlowSummary summary, Portal p1, Portal p2, string taint
where summary.hasFlow(p1, p2, taint)
select summary, p1, p2, taint