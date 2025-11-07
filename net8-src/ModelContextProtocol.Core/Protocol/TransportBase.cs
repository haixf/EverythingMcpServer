using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;
using System.IO;
using System.Threading.Channels;

namespace ModelContextProtocol.Protocol;

/// <summary>
/// Provides a base class for implementing <see cref="ITransport"/>.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="TransportBase"/> class provides core functionality required by most <see cref="ITransport"/>
/// implementations, including message channel management, connection state tracking, and logging support.
/// </para>
/// <para>
/// Custom transport implementations should inherit from this class and implement the abstract
/// <see cref="SendMessageAsync(JsonRpcMessage, CancellationToken)"/> and <see cref="DisposeAsync()"/> methods
/// to handle the specific transport mechanism being used.
/// </para>
/// </remarks>
public abstract partial class TransportBase : ITransport
{
    private readonly Channel<JsonRpcMessage> _messageChannel;
    private readonly ILogger _logger;
    private volatile int _state = StateInitial;

    /// <summary>The transport has not yet been connected.</summary>
    private const int StateInitial = 0;
    /// <summary>The transport is connected.</summary>
    private const int StateConnected = 1;
    /// <summary>The transport was previously connected and is now disconnected.</summary>
    private const int StateDisconnected = 2;

    /// <summary>
    /// Initializes a new instance of the <see cref="TransportBase"/> class.
    /// </summary>
    protected TransportBase(string name, ILoggerFactory? loggerFactory)
        : this(name, null, loggerFactory)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TransportBase"/> class with a specified channel to back <see cref="MessageReader"/>.
    /// </summary>
    internal TransportBase(string name, Channel<JsonRpcMessage>? messageChannel, ILoggerFactory? loggerFactory)
    {
        Name = name;
        _logger = loggerFactory?.CreateLogger(GetType()) ?? NullLogger.Instance;

        // Unbounded channel to prevent blocking on writes. Ensure AutoDetectingClientSessionTransport matches this.
        _messageChannel = messageChannel ?? Channel.CreateUnbounded<JsonRpcMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <summary>Gets the logger used by this transport.</summary>
    private protected ILogger Logger => _logger;

    /// <inheritdoc/>
    public virtual string? SessionId { get; protected set; }

    /// <summary>
    /// Gets the name that identifies this transport endpoint in logs.
    /// </summary>
    /// <remarks>
    /// This name is used in log messages to identify the source of transport-related events.
    /// </remarks>
    protected string Name { get; }

    /// <inheritdoc/>
    public bool IsConnected => _state == StateConnected;

    /// <inheritdoc/>
    public ChannelReader<JsonRpcMessage> MessageReader => _messageChannel.Reader;

    /// <inheritdoc/>
    public abstract Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default);

    /// <inheritdoc/>
    public abstract ValueTask DisposeAsync();

    /// <summary>
    /// Writes a message to the message channel.
    /// </summary>
    /// <param name="message">The message to write.</param>
    /// <param name="cancellationToken">The <see cref="CancellationToken"/> to monitor for cancellation requests. The default is <see cref="CancellationToken.None"/>.</param>
    protected async Task WriteMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Transport is not connected.");
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var messageId = (message as JsonRpcMessageWithId)?.Id.ToString() ?? "(no id)";
            LogTransportReceivedMessage(Name, messageId);
        }

        bool wrote = _messageChannel.Writer.TryWrite(message);
        Debug.Assert(wrote || !IsConnected, "_messageChannel is unbounded; this should only ever return false if the channel has been closed.");
    }

    /// <summary>
    /// Sets the transport to a connected state.
    /// </summary>
    protected void SetConnected()
    {
        while (true)
        {
            int state = _state;
            switch (state)
            {
                case StateInitial:
                    if (Interlocked.CompareExchange(ref _state, StateConnected, StateInitial) == StateInitial)
                    {
                        return;
                    }
                    break;

                case StateConnected:
                    return;

                case StateDisconnected:
                    throw new IOException("Transport is already disconnected and can't be reconnected.");

                default:
                    Debug.Fail($"Unexpected state: {state}");
                    return;
            }
        }
    }

    /// <summary>
    /// Sets the transport to a disconnected state.
    /// </summary>
    /// <param name="error">Optional error information associated with the transport disconnecting. Should be <see langwor="null"/> if the disconnect was graceful and expected.</param>
    protected void SetDisconnected(Exception? error = null)
    {
        int state = _state;
        switch (state)
        {
            case StateInitial:
            case StateConnected:
                _state = StateDisconnected;
                _messageChannel.Writer.TryComplete(error);
                break;

            case StateDisconnected:
                return;

            default:
                Debug.Fail($"Unexpected state: {state}");
                break;
        }
    }

    private protected void LogTransportConnectFailed(string endpointName, Exception exception) =>
        _logger.LogError(exception, "{EndpointName} transport connect failed.", endpointName);

    private protected void LogTransportSendFailed(string endpointName, string messageId, Exception exception) =>
        _logger.LogError(exception, "{EndpointName} transport send failed for message ID '{MessageId}'.", endpointName, messageId);

    private protected void LogTransportEnteringReadMessagesLoop(string endpointName) =>
        _logger.LogInformation("{EndpointName} transport reading messages.", endpointName);

    private protected void LogTransportEndOfStream(string endpointName) =>
        _logger.LogInformation("{EndpointName} transport completed reading messages.", endpointName);

    private protected void LogTransportReceivedMessageSensitive(string endpointName, string message) =>
        _logger.LogTrace("{EndpointName} transport received message. Message: '{Message}'.", endpointName, message);

    private protected void LogTransportReceivedMessage(string endpointName, string messageId) =>
        _logger.LogDebug("{EndpointName} transport received message with ID '{MessageId}'.", endpointName, messageId);

    private protected void LogTransportMessageParseUnexpectedTypeSensitive(string endpointName, string message) =>
        _logger.LogTrace("{EndpointName} transport received unexpected message. Message: '{Message}'.", endpointName, message);

    private protected void LogTransportMessageParseFailed(string endpointName, Exception exception) =>
        _logger.LogInformation(exception, "{EndpointName} transport message parsing failed.", endpointName);

    private protected void LogTransportMessageParseFailedSensitive(string endpointName, string message, Exception exception) =>
        _logger.LogTrace(exception, "{EndpointName} transport message parsing failed. Message: '{Message}'.", endpointName, message);

    private protected void LogTransportReadMessagesCancelled(string endpointName) =>
        _logger.LogInformation("{EndpointName} transport message reading canceled.", endpointName);

    private protected void LogTransportReadMessagesFailed(string endpointName, Exception exception) =>
        _logger.LogWarning(exception, "{EndpointName} transport message reading failed.", endpointName);

    private protected void LogTransportShuttingDown(string endpointName) =>
        _logger.LogInformation("{EndpointName} shutting down.", endpointName);

    private protected void LogTransportShutdownFailed(string endpointName, Exception exception) =>
        _logger.LogWarning(exception, "{EndpointName} shutdown failed.", endpointName);

    private protected void LogTransportCleanupReadTaskFailed(string endpointName, Exception exception) =>
        _logger.LogWarning(exception, "{EndpointName} shutdown failed waiting for message reading completion.", endpointName);

    private protected void LogTransportShutDown(string endpointName) =>
        _logger.LogInformation("{EndpointName} shut down.", endpointName);

    private protected void LogTransportMessageReceivedBeforeConnected(string endpointName) =>
        _logger.LogWarning("{EndpointName} received message before connected.", endpointName);

    private protected void LogTransportEndpointEventInvalid(string endpointName) =>
        _logger.LogWarning("{EndpointName} endpoint event received out of order.", endpointName);

    private protected void LogTransportEndpointEventParseFailed(string endpointName, Exception exception) =>
        _logger.LogWarning(exception, "{EndpointName} failed to parse event.", endpointName);

    private protected void LogTransportEndpointEventParseFailedSensitive(string endpointName, string message, Exception exception) =>
        _logger.LogWarning(exception, "{EndpointName} failed to parse event. Message: '{Message}'.", endpointName, message);
}