/* Models return types. */


import dotnet

/**
 * Holds if callable `c` could return a `null`.
 */
predicate returnsNull(DotNet::Callable c) {
  none()
}

/**
 * Holds if this predicate could return a value that is definitely non-null.
 */
predicate returnsNonNull(DotNet::Callable c) {
  none()
}

predicate returnsVoid(DotNet::Callable c) {
  none()
}

/**
 * Holds if callable `c` returns a value that is untracked - perhaps from an unknown library,
 * or from a virtual dispatch call or some other dataflow source.
 */
predicate returnsUnknown(DotNet::Callable c) {
  none()
}

select 1
