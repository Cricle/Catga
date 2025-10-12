## Release 2.0.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
CATGA001 | Usage | Info | Handler not registered
CATGA002 | Usage | Warning | Invalid handler signature
CATGA003 | Naming | Info | Missing Async suffix for async handlers
CATGA004 | Design | Info | Missing CancellationToken parameter
CATGA005 | Performance | Warning | Blocking call detected (.Result or .Wait)
CATGA006 | Performance | Warning | Excessive allocations in hot path
CATGA007 | Performance | Info | Missing ConfigureAwait(false)
CATGA008 | Performance | Warning | LINQ in hot path
CATGA009 | Performance | Info | String concatenation in loop
CATGA010 | Best Practice | Warning | HttpContext in handler
CATGA011 | Best Practice | Warning | Exception in event handler
CATGA012 | Best Practice | Info | Missing CancellationToken propagation
CATGA013 | Best Practice | Info | Handler should implement IDisposable
CATGA014 | Best Practice | Info | Use record type for messages
CATGA015 | Best Practice | Info | Enable nullable reference types

