﻿using Grpc.Core;
using MagicOnion.Client.EmbeddedServices;
using MagicOnion.Server;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MagicOnion.Client
{
    public class ChannelContext : IDisposable
    {
        public const string HeaderKey = "connection_id";

        readonly Channel channel;
        readonly bool useSameId;
        readonly Func<string> connectionIdFactory;

        bool isDisposed;
        int currentRetryCount = 0;
        Task connectingTask;
        DuplexStreamingResult<bool, bool> latestStreamingResult;
        TaskCompletionSource<object> waitConnectComplete;
        LinkedList<Action> disconnectedActions = new LinkedList<Action>();

        string connectionId;
        public string ConnectionId
        {
            get
            {
                if (isDisposed) throw new ObjectDisposedException("ChannelContext");
                return connectionId;
            }
        }

        public ChannelContext(Channel channel, Func<string> connectionIdFactory = null, bool useSameId = true)
        {
            this.channel = channel;
            this.useSameId = useSameId;
            this.connectionIdFactory = connectionIdFactory ?? (() => Guid.NewGuid().ToString());
            if (useSameId)
            {
                this.connectionId = this.connectionIdFactory();
            }
            this.waitConnectComplete = new TaskCompletionSource<object>();
            connectingTask = ConnectAlways();
        }

        public Task WaitConnectComplete()
        {
            return waitConnectComplete.Task;
        }

        public IDisposable RegisterDisconnectedAction(Action action)
        {
            var node = disconnectedActions.AddLast(action);
            return new UnregisterToken(node);
        }

        async Task ConnectAlways()
        {
            while (true)
            {
                try
                {
                    if (isDisposed) return;
                    if (channel.State == ChannelState.Shutdown) return;

                    await channel.ConnectAsync();

                    if (isDisposed) return;

                    var connectionId = (useSameId) ? this.connectionId : connectionIdFactory();
                    var client = new HeartbeatClient(channel, connectionId);
                    latestStreamingResult.Dispose();
                    latestStreamingResult = await client.Connect();
                    
                    // wait connect complete.
                    await latestStreamingResult.ResponseStream.MoveNext();

                    this.connectionId = connectionId;
                    currentRetryCount = 0;
                    waitConnectComplete.TrySetResult(new object());

                    try
                    {
                        // now channelstate is ready and wait changed.
                        await Task.WhenAny(channel.WaitForStateChangedAsync(ChannelState.Ready), latestStreamingResult.ResponseStream.MoveNext()).ConfigureAwait(false);
                    }
                    finally
                    {
                        waitConnectComplete = new TaskCompletionSource<object>();
                        foreach (var action in disconnectedActions)
                        {
                            action();
                        }
                    }
                }
                catch (Exception ex)
                {
                    GrpcEnvironment.Logger.Error(ex, "Reconnect Failed, Retrying:" + currentRetryCount++);
                }
            }
        }

        // TODO:more createClient overload.
        public T CreateClient<T>()
            where T : IService<T>
        {
            return MagicOnionClient.Create<T>(channel)
                .WithHeaders(new Metadata { { ChannelContext.HeaderKey, ConnectionId } });
        }

        public T CreateClient<T>(Metadata metadata)
            where T : IService<T>
        {
            var newMetadata = new Metadata();
            for (int i = 0; i < metadata.Count; i++)
            {
                newMetadata.Add(metadata[i]);
            }
            newMetadata.Add(ChannelContext.HeaderKey, ConnectionId);

            return MagicOnionClient.Create<T>(channel).WithHeaders(newMetadata);
        }

        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;

            waitConnectComplete.TrySetCanceled();
            latestStreamingResult.Dispose();
        }

        class UnregisterToken : IDisposable
        {
            LinkedListNode<Action> node;

            public UnregisterToken(LinkedListNode<Action> node)
            {
                this.node = node;
            }

            public void Dispose()
            {
                if (node != null)
                {
                    node.List.Remove(node);
                    node = null;
                }
            }
        }
    }
}