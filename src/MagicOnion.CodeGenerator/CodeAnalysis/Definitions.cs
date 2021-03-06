﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MagicOnion.CodeAnalysis
{
    public enum MethodType
    {
        Unary = 0,
        ClientStreaming = 1,
        ServerStreaming = 2,
        DuplexStreaming = 3
    }

    public class InterfaceDefintion
    {
        public string Name { get; set; }
        public string Namespace { get; set; }
        public bool IsServiceDifinition { get; set; }
        public string[] InterfaceNames { get; set; }
        public MethodDefinition[] Methods { get; set; }

        public override string ToString()
        {
            return (Namespace != null)
                ? Namespace + "." + Name
                : Name;
        }
    }

    public class MethodDefinition
    {
        public string Name { get; set; }
        public MethodType MethodType { get; set; }
        public string RequestType { get; set; }
        public string ResponseType { get; set; }
        public ParameterDefinition[] Parameters { get; set; }

        public string ReturnType
        {
            get
            {
                switch (MethodType)
                {
                    case MethodType.Unary:
                        return $"UnaryResult<{ResponseType}>";
                    case MethodType.ClientStreaming:
                        return $"ClientStreamingResult<{RequestType}, {ResponseType}>";
                    case MethodType.ServerStreaming:
                        return $"ServerStreamingResult<{ResponseType}>";
                    case MethodType.DuplexStreaming:
                        return $"DuplexStreamingResult<{RequestType}, {ResponseType}>";
                    default:
                        throw new Exception();
                }
            }
        }

        public string RequestMarshallerType
        {
            get
            {
                switch (MethodType)
                {
                    case MethodType.Unary:
                    case MethodType.ServerStreaming:
                        if (Parameters.Length == 0)
                        {
                            return "byte[]";
                        }
                        else if (Parameters.Length == 1)
                        {
                            return RequestType;
                        }
                        else
                        {
                            var typeArgs = string.Join(", ", Parameters.Select(x => x.TypeName));
                            return $"DynamicArgumentTuple<{typeArgs}>";
                        }
                    case MethodType.ClientStreaming:
                    case MethodType.DuplexStreaming:
                    default:
                        return RequestType;
                }
            }
        }

        public string RequestFormatter(string resolverName)
        {
            if (Parameters.Length == 1 || MethodType == MethodType.ClientStreaming || MethodType == MethodType.DuplexStreaming)
            {
                return $"ZeroFormatter.Formatters.Formatter<{resolverName}, {RequestType}>.Default";
            }
            else if (Parameters.Length == 0)
            {
                return $"ZeroFormatter.Formatters.Formatter<{resolverName}, byte[]>.Default";
            }
            else
            {
                var typeArgs = string.Join(", ", Parameters.Select(x => x.TypeName));
                var parameterValues = string.Join(", ", Parameters.Select(x => x.DefaultValue));
                return $"new DynamicArgumentTupleFormatter<{resolverName}, {typeArgs}>({parameterValues})";
            }
        }

        public string RequestObject()
        {
            if (Parameters.Length == 0)
            {
                return $"MagicOnionMarshallers.EmptyBytes";
            }
            else if (Parameters.Length == 1)
            {
                return Parameters[0].ParameterName;
            }
            else
            {
                var typeArgs = string.Join(", ", Parameters.Select(x => x.TypeName));
                var parameterNames = string.Join(", ", Parameters.Select(x => x.ParameterName));
                return $"new DynamicArgumentTuple<{typeArgs}>({parameterNames})";
            }
        }

        public override string ToString()
        {
            return $"{ReturnType} {Name}({string.Join(", ", Parameters.Select(x => x.ToString()))})";
        }
    }

    public class ParameterDefinition
    {
        public string TypeName { get; set; }
        public string ParameterName { get; set; }
        public bool HasDefaultValue { get; set; }
        public string DefaultValue { get; set; }

        public override string ToString()
        {
            if (HasDefaultValue)
            {
                return TypeName + " " + ParameterName + " = " + DefaultValue;
            }
            else
            {
                return TypeName + " " + ParameterName;
            }
        }
    }
}
