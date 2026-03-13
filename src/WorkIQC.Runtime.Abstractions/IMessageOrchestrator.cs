using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WorkIQC.Runtime.Abstractions.Models;

namespace WorkIQC.Runtime.Abstractions;

public interface IMessageOrchestrator
{
    Task<SendMessageResponse> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default);
    IAsyncEnumerable<StreamingDelta> StreamResponseAsync(string sessionId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<ToolEvent> ObserveToolEventsAsync(string sessionId, CancellationToken cancellationToken = default);
}
