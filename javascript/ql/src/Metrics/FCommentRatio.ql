/**
 * @name Comment ratio in files
 * @description The percentage of lines in a file that contain comments.
 * @kind treemap
 * @treemap.warnOn lowValues
 * @metricType file
 * @metricAggregate avg max
 * @precision very-high
 * @tags maintainability
 * @id js/comment-ratio-per-file
 */

import javascript

from File f, int n
where n = strictsum(TopLevel tl | tl = f.getATopLevel() | tl.getNumberOfLines())
select f, 100.0 * ((float)f.getNumberOfLinesOfComments() / (float)n) as ratio
order by ratio desc
