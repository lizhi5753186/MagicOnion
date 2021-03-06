﻿using Grpc.Core;
using MagicOnion.Server;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace MagicOnion.Server
{
    public class ServiceContext
    {
        Dictionary<string, object> items;

        /// <summary>Object storage per invoke.</summary>
        public IDictionary<string, object> Items
        {
            get
            {
                if (items == null) items = new Dictionary<string, object>();
                return items;
            }
        }

        public DateTime Timestamp { get; private set; }

        public Type ServiceType { get; private set; }

        public MethodInfo MethodInfo { get; private set; }

        /// <summary>Cached Attributes both service and method.</summary>
        public ILookup<Type, Attribute> AttributeLookup { get; private set; }

        public MethodType MethodType { get; private set; }

        /// <summary>Raw gRPC Context.</summary>
        public ServerCallContext CallContext { get; private set; }

        // internal, used from there methods.
        internal object RequestMarshaller { get; set; }
        internal object ResponseMarshaller { get; set; }
        internal byte[] Request { get; set; }
        internal IAsyncStreamReader<byte[]> RequestStream { get; set; }
        internal IAsyncStreamWriter<byte[]> ResponseStream { get; set; }
        internal byte[] Result { get; set; }
        internal IMagicOnionLogger MagicOnionLogger { get; private set; }

        public ServiceContext(Type serviceType, MethodInfo methodInfo, ILookup<Type, Attribute> attributeLookup, MethodType methodType, ServerCallContext context, IMagicOnionLogger logger)
        {
            this.ServiceType = serviceType;
            this.MethodInfo = methodInfo;
            this.AttributeLookup = attributeLookup;
            this.MethodType = methodType;
            this.CallContext = context;
            this.Timestamp = DateTime.UtcNow;
            this.MagicOnionLogger = logger;
        }
    }

    public class ClientStreamingContext<TRequest, TResponse> : IAsyncStreamReader<TRequest>
    {
        readonly ServiceContext context;
        readonly IAsyncStreamReader<byte[]> inner;
        readonly Marshaller<TRequest> marshaller;

        public ClientStreamingContext(ServiceContext context)
        {
            this.context = context;
            this.marshaller = (Marshaller<TRequest>)context.RequestMarshaller;
            this.inner = new LoggableStreamReader<byte[]>(context.MagicOnionLogger, context, context.RequestStream);
        }

        public ServiceContext ServiceContext { get { return context; } }

        public TRequest Current { get; private set; }

        public async Task<bool> MoveNext(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (await inner.MoveNext(cancellationToken))
            {
                this.Current = marshaller.Deserializer(inner.Current);
                return true;
            }
            else
            {
                return false;
            }
        }

        public void Dispose()
        {
            inner.Dispose();
        }

        public async Task ForEachAsync(Action<TRequest> action)
        {
            while (await MoveNext(CancellationToken.None)) // ClientResponseStream is not supported CancellationToken.
            {
                action(Current);
            }
        }

        public async Task ForEachAsync(Func<TRequest, Task> asyncAction)
        {
            while (await MoveNext(CancellationToken.None))
            {
                await asyncAction(Current);
            }
        }

        public ClientStreamingResult<TRequest, TResponse> Result(TResponse result)
        {
            var marshaller = context.ResponseMarshaller;
            if (marshaller == null) throw new Exception();

            var serializer = marshaller as Marshaller<TResponse>;
            if (serializer == null) throw new Exception();

            var bytes = serializer.Serializer(result);
            context.Result = bytes;

            return default(ClientStreamingResult<TRequest, TResponse>); // dummy
        }

        public ClientStreamingResult<TRequest, TResponse> ReturnStatus(StatusCode statusCode, string detail)
        {
            context.CallContext.Status = new Status(statusCode, detail);

            return default(ClientStreamingResult<TRequest, TResponse>); // dummy
        }
    }

    public class ServerStreamingContext<TResponse> : IAsyncStreamWriter<TResponse>
    {
        readonly ServiceContext context;
        readonly IAsyncStreamWriter<byte[]> inner;
        readonly Marshaller<TResponse> marshaller;

        public ServerStreamingContext(ServiceContext context)
        {
            this.context = context;
            this.marshaller = (Marshaller<TResponse>)context.ResponseMarshaller;
            this.inner = new LoggableStreamWriter<byte[]>(context.MagicOnionLogger, context, context.ResponseStream);
        }

        public ServiceContext ServiceContext { get { return context; } }

        public WriteOptions WriteOptions
        {
            get
            {
                return inner.WriteOptions;
            }

            set
            {
                inner.WriteOptions = value;
            }
        }

        public Task WriteAsync(TResponse message)
        {
            var bytes = marshaller.Serializer(message);
            return inner.WriteAsync(bytes);
        }

        public ServerStreamingResult<TResponse> Result()
        {
            return default(ServerStreamingResult<TResponse>); // dummy
        }

        public ServerStreamingResult<TResponse> ReturnStatus(StatusCode statusCode, string detail)
        {
            context.CallContext.Status = new Status(statusCode, detail);

            return default(ServerStreamingResult<TResponse>); // dummy
        }
    }

    public class DuplexStreamingContext<TRequest, TResponse> : IAsyncStreamReader<TRequest>, IServerStreamWriter<TResponse>
    {
        readonly ServiceContext context;
        readonly IAsyncStreamReader<byte[]> innerReader;
        readonly IAsyncStreamWriter<byte[]> innerWriter;
        readonly Marshaller<TRequest> requestMarshaller;
        readonly Marshaller<TResponse> responseMarshaller;

        public DuplexStreamingContext(ServiceContext context)
        {
            this.context = context;
            this.innerReader = new LoggableStreamReader<byte[]>(context.MagicOnionLogger, context, context.RequestStream);
            this.innerWriter = new LoggableStreamWriter<byte[]>(context.MagicOnionLogger, context, context.ResponseStream);
            this.requestMarshaller = (Marshaller<TRequest>)context.RequestMarshaller;
            this.responseMarshaller = (Marshaller<TResponse>)context.ResponseMarshaller;
        }

        public ServiceContext ServiceContext { get { return context; } }

        /// <summary>IAsyncStreamReader Methods.</summary>
        public TRequest Current { get; private set; }

        /// <summary>IAsyncStreamReader Methods.</summary>
        public async Task<bool> MoveNext(CancellationToken cancellationToken = default(CancellationToken))
        {
            if (await innerReader.MoveNext(cancellationToken))
            {
                this.Current = requestMarshaller.Deserializer(innerReader.Current);
                return true;
            }
            else
            {
                return false;
            }
        }

        /// <summary>IAsyncStreamReader Methods.</summary>
        public void Dispose()
        {
            innerReader.Dispose();
        }

        /// <summary>
        /// IServerStreamWriter Methods.
        /// </summary>
        public WriteOptions WriteOptions
        {
            get
            {
                return innerWriter.WriteOptions;
            }

            set
            {
                innerWriter.WriteOptions = value;
            }
        }

        /// <summary>
        /// IServerStreamWriter Methods.
        /// </summary>
        public Task WriteAsync(TResponse message)
        {
            var bytes = responseMarshaller.Serializer(message);
            return innerWriter.WriteAsync(bytes);
        }

        public DuplexStreamingResult<TRequest, TResponse> Result()
        {
            return default(DuplexStreamingResult<TRequest, TResponse>); // dummy
        }

        public DuplexStreamingResult<TRequest, TResponse> ReturnStatus(StatusCode statusCode, string detail)
        {
            context.CallContext.Status = new Status(statusCode, detail);

            return default(DuplexStreamingResult<TRequest, TResponse>); // dummy
        }
    }
}