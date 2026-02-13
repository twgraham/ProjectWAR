# Issues to Create from PR Comments

This document contains issues identified in PR #10, #11, and #12 that should be created in the repository.

## Issue 1: Implement bounded channels with backpressure in ClientConnection

**Source**: PR #10

**Title**: Implement bounded channels with backpressure in ClientConnection

**Description**:
`_receiveQueue` and `_sendQueue` in `ClientConnection.cs` use `Channel.CreateUnbounded()`, which allows unbounded memory growth from slow handlers or slow clients. This creates a DoS risk.

**Problem**:
- Current implementation uses `Channel.CreateUnbounded()` for both receive and send queues
- No limits on memory growth
- Slow handlers or slow clients can cause memory exhaustion
- Potential DoS vulnerability

**Proposed Solution**:
Replace with `Channel.CreateBounded()` with:
- Appropriate capacity limits
- Backpressure handling strategy
- Consider different strategies for receive vs send queues

**Location**: `src/FrameWork/NetWork/V4/ClientConnection.cs:83-84`

**Labels**: enhancement, security

---

## Issue 2: Fix potential deadlock in ClientConnection.Disconnect()

**Source**: PR #11

**Title**: Fix potential deadlock in ClientConnection.Disconnect()

**Description**:
The `ClientConnection.Disconnect()` method can cause a deadlock when called from within the receive/process/send loops.

**Problem**:
```csharp
public void Disconnect(DisconnectReason reason)
{
    if (_disposed) return;
    Disconnected?.Invoke(reason);
    Dispose();  // ← Waits on tasks that may include the calling task
}
```

`Disconnect()` calls `Dispose()` synchronously, which performs `Task.WaitAll()` on receive/process/send loops. If `Disconnect()` is invoked from within these loops (e.g., via `OnDispatchError()`), the task waits on itself, causing a deadlock.

**Proposed Solution**:
- Make `Dispose()` asynchronous or
- Use a flag-based approach to signal shutdown without blocking or
- Ensure `Disconnect()` never blocks on the task that might call it

**Location**: `ClientConnection.Disconnect()` method

**Labels**: bug, concurrency

---

## Issue 3: Optimize or secure hex dump logging in ClientConnection

**Source**: PR #12 (comment ID: 2802402310)

**Title**: Review and optimize hex dump logging

**Description**:
Hex dump logging in `ClientConnection` is expensive and may leak sensitive data.

**Problem**:
- Performance impact from generating hex dumps for every packet
- Potential security issue if sensitive data (passwords, session tokens, etc.) is logged
- Logs may grow very large in production

**Proposed Solution**:
- Add conditional compilation or runtime flag to disable hex dumps in production
- Consider logging only at higher log levels (e.g., TRACE/DEBUG)
- Sanitize sensitive data before logging
- Add configuration option to control hex dump logging

**Labels**: performance, security, logging

---

## Issue 4: Refactor Dispose() to avoid deadlock with Task.WaitAll

**Source**: PR #12 (comment ID: 2802403082)

**Title**: Refactor Dispose() to prevent Task.WaitAll deadlock

**Description**:
The `Dispose()` method uses `Task.WaitAll` which can deadlock if called from a task it's waiting on.

**Problem**:
- `Task.WaitAll` blocks synchronously
- If `Dispose()` is called from within one of the tasks being waited on, it creates a deadlock
- Related to Issue #2 but focuses specifically on the `Dispose()` implementation

**Proposed Solution**:
- Refactor to use asynchronous disposal pattern (`IAsyncDisposable`)
- Use `Task.WhenAll` with async/await instead of `Task.WaitAll`
- Implement cancellation token based shutdown instead of synchronous waiting

**Location**: `ClientConnection.Dispose()` method

**Labels**: bug, concurrency, refactoring

---

## Issue 5: Handle OperationCanceledException in AcceptLoopAsync

**Source**: PR #12 (comment ID: 2802404530)

**Title**: Add OperationCanceledException handling to AcceptLoopAsync

**Description**:
`AcceptLoopAsync` needs proper handling of `OperationCanceledException` for clean shutdowns.

**Problem**:
- `AcceptTcpClientAsync` can throw `OperationCanceledException` during shutdown
- Without proper handling, normal shutdowns may appear as errors
- Similar issue to what was fixed in `ReceiveLoopAsync` (see PR #12)

**Proposed Solution**:
Add a dedicated catch block for `OperationCanceledException`:
```csharp
catch (OperationCanceledException)
{
    // Normal shutdown via cancellation token - exit cleanly
    return;
}
```

**Location**: `AcceptLoopAsync` method

**Labels**: bug, enhancement

---

## Issue 6: Fix Stop() method to properly fire ClientDisconnected event

**Source**: PR #12 (comment ID: 2802405991)

**Title**: Stop() should trigger ClientDisconnected lifecycle events

**Description**:
The `Stop()` method disposes connections directly, bypassing the `ClientDisconnected` event.

**Problem**:
- `Stop()` disposes connections without firing the `ClientDisconnected` event
- Event handlers that depend on `ClientDisconnected` won't execute during shutdown
- Inconsistent behavior compared to normal disconnection flow
- May lead to resource leaks or improper cleanup if event handlers perform important cleanup

**Proposed Solution**:
- Ensure `Stop()` triggers `ClientDisconnected` event before disposing
- Or document that `Stop()` is for emergency shutdown and won't fire events
- Make behavior consistent and predictable

**Labels**: bug, lifecycle-events

---

## Issue 7: Resolve DI registration ambiguity for IPacketDispatcher

**Source**: PR #12 (comment ID: 2802410475)

**Title**: Fix last-registration-wins issue for IPacketDispatcher in DI container

**Description**:
When multiple packet groups register `IPacketDispatcher`, only the last registration is used due to DI container behavior.

**Problem**:
- Multiple packet groups may register `IPacketDispatcher` implementations
- DI containers typically use "last registration wins" strategy
- This causes only the final registered dispatcher to be used
- Other dispatchers are silently ignored
- Can lead to missing packet handlers

**Proposed Solution**:
- Use named registrations or keyed services
- Register a composite dispatcher that delegates to all registered dispatchers
- Use `IEnumerable<IPacketDispatcher>` and resolve all implementations
- Add validation to detect and report ambiguous registrations

**Location**: DI registration code for packet handling

**Labels**: bug, dependency-injection, architecture

---

## Summary

Total issues to create: **7**

- **Security**: 2 issues (#1, #3)
- **Concurrency/Deadlock**: 3 issues (#2, #4, #5)
- **Lifecycle/Events**: 1 issue (#6)
- **Architecture/DI**: 1 issue (#7)

All issues are related to the `ClientConnection` and network handling code in `src/FrameWork/NetWork/V4/`.
