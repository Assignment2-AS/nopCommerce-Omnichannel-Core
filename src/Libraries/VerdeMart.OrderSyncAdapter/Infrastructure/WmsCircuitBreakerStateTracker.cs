using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace VerdeMart.OrderSyncAdapter.Infrastructure;

public sealed class WmsCircuitBreakerStateTracker
{
    private readonly Channel<bool> _closedTransitions = Channel.CreateUnbounded<bool>();
    private readonly object _gate = new();
    private bool _isClosed = true;

    public bool IsClosed
    {
        get
        {
            lock (_gate)
            {
                return _isClosed;
            }
        }
    }

    public void MarkOpened()
    {
        lock (_gate)
        {
            _isClosed = false;
        }
    }

    public void MarkHalfOpen()
    {
        lock (_gate)
        {
            _isClosed = false;
        }
    }

    public void MarkClosed()
    {
        var shouldSignal = false;

        lock (_gate)
        {
            if (!_isClosed)
            {
                shouldSignal = true;
            }

            _isClosed = true;
        }

        if (shouldSignal)
        {
            _closedTransitions.Writer.TryWrite(true);
        }
    }

    public async Task WaitForClosedTransitionAsync(CancellationToken cancellationToken)
    {
        await _closedTransitions.Reader.ReadAsync(cancellationToken);
    }
}
