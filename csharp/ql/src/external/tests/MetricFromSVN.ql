/**
 * @name Metric from SVN
 * @description Find number of commits for a file
 * @treemap.warnOn lowValues
 * @metricType file
 * @kind treemap
 * @deprecated
 */

import csharp

import external.VCS

predicate numCommits(File f, int i) {
  i = count(Commit e | e.getAnAffectedFile() = f)
}

from File f, int i
where numCommits(f, i)
select f, i
