using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Reflection;

namespace ExpressionTreeJitContentionTest;

public delegate void SerializeDelegateSignature(object obj, ISerializationContext? context, bool alwaysWrite);

public interface ISerializationContext
{

}

public interface ISerializationManager
{
    DataNode WriteValue<T>(T value, bool alwaysWrite = false, ISerializationContext? context = null,
        bool notNullableOverride = false);
}

public sealed class SerializationManager : ISerializationManager
{
    private delegate DataNode WriteGenericDelegate<T>(
        T value,
        bool alwaysWrite,
        ISerializationContext? context);

    // private readonly ConcurrentDictionary<(Type, bool), WriteBoxingDelegate> _writeBoxingDelegates = new();
    private readonly ConcurrentDictionary<(Type baseType, Type actualType, bool), object> _writeGenericBaseDelegates = new();
    private readonly ConcurrentDictionary<(Type, bool), object> _writeGenericDelegates = new();

    public DataNode WriteValue<T>(T value, bool alwaysWrite = false, ISerializationContext? context = null,
        bool notNullableOverride = false)
    {
        if(value == null)
        {
            CanWriteNullCheck(typeof(T), notNullableOverride);
            return ValueDataNode.Null();
        }

        var node = GetOrCreateWriteGenericDelegate(value, notNullableOverride)(value, alwaysWrite, context);

        if (typeof(T) == typeof(object))
            node.Tag = "!type:" + value.GetType().Name;

        return node;
    }

    private WriteGenericDelegate<T> GetOrCreateWriteGenericDelegate<T>(T value, bool notNullableOverride)
    {
        static object ValueFactory(Type baseType, Type actualType, bool notNullableOverride, SerializationManager serializationManager)
        {
            throw new NotImplementedException();
        }

        var type = typeof(T);
        if (!type.IsSealed) // abstract classes, virtual classes, and interfaces.
        {
            return (WriteGenericDelegate<T>)_writeGenericBaseDelegates.GetOrAdd((type, value!.GetType(), notNullableOverride),
                static (tuple, manager) => ValueFactory(tuple.baseType, tuple.actualType, tuple.Item3, manager), this);
        }

        return (WriteGenericDelegate<T>) _writeGenericDelegates
            .GetOrAdd((type, notNullableOverride), static (tuple, manager) => ValueFactory(tuple.Item1, tuple.Item1, tuple.Item2, manager), this);
    }

    private void CanWriteNullCheck(Type type, bool notNullableOverride)
    {
        if (!type.IsNullable() || notNullableOverride)
        {
            throw new NullNotAllowedException();
        }
    }
}

public sealed class NullNotAllowedException : Exception
{
    public override string Message => "Null value provided for reading but type was not nullable!";
}

public abstract class DataNode
{
    public string? Tag;
    public NodeMark Start;
    public NodeMark End;

    public DataNode(NodeMark start, NodeMark end)
    {
        Start = start;
        End = end;
    }

    public abstract bool IsEmpty { get; }
    public virtual bool IsNull { get; init; } = false;

    public abstract DataNode Copy();

    /// <summary>
    ///     This function will return a data node that contains only the elements within this data node that do not
    ///     have an equivalent entry in some other data node.
    /// </summary>
    public abstract DataNode? Except(DataNode node);

    public abstract DataNode PushInheritance(DataNode parent);

    public T CopyCast<T>() where T : DataNode
    {
        return (T) Copy();
    }

    public void Write(TextWriter writer)
    {
        /*var yaml = this.ToYamlNode();
        var stream = new YamlStream { new(yaml) };
        stream.Save(new YamlMappingFix(new Emitter(writer)), false);*/
    }

    public override string ToString()
    {
        StringWriter sw = new StringWriter();
        Write(sw);
        return sw.ToString();
    }
}

public abstract class DataNode<T> : DataNode where T : DataNode<T>
{
    protected DataNode(NodeMark start, NodeMark end) : base(start, end)
    {
    }

    public abstract override T Copy();

    public abstract T? Except(T node);

    public abstract T PushInheritance(T node);

    public override DataNode? Except(DataNode node)
    {
        return node is not T tNode ? throw new InvalidNodeTypeException() : Except(tNode);
    }

    public override DataNode PushInheritance(DataNode parent)
    {
        return parent is not T tNode ? throw new InvalidNodeTypeException() : PushInheritance(tNode);
    }
}

public sealed class ValueDataNode : DataNode<ValueDataNode>
{
        public static ValueDataNode Null() => new((string?)null);

        public ValueDataNode() : this(string.Empty) {}

        public ValueDataNode(string? value) : base(NodeMark.Invalid, NodeMark.Invalid)
        {
            Value = value ?? string.Empty;
            IsNull = value == null;
        }

        /*
        public ValueDataNode(YamlScalarNode node) : base(node.Start, node.End)
        {
            IsNull = CalculateIsNullValue(node.Value, node.Tag, node.Style);
            Value = node.Value ?? string.Empty;
            Tag = node.Tag.IsEmpty ? null : node.Tag.Value;
        }
        */

        /*
        public ValueDataNode(Scalar scalar) : base(scalar.Start, scalar.End)
        {
            IsNull = CalculateIsNullValue(scalar.Value, scalar.Tag, scalar.Style);
            Value = scalar.Value;
            Tag = scalar.Tag.IsEmpty ? null : scalar.Tag.Value;
        }

        private bool CalculateIsNullValue(string? content, TagName tag, ScalarStyle style)
        {
            return style != ScalarStyle.DoubleQuoted && style != ScalarStyle.SingleQuoted &&
                   (IsNullLiteral(content) || string.IsNullOrWhiteSpace(content) && tag.IsEmpty);
        }
        */

        /*public static explicit operator YamlScalarNode(ValueDataNode node)
        {
            if (node.IsNull)
            {
                return new YamlScalarNode("null"){Tag = node.Tag};
            }

            return new YamlScalarNode(node.Value)
            {
                Tag = node.Tag,
                Style = IsNullLiteral(node.Value) || string.IsNullOrWhiteSpace(node.Value) ? ScalarStyle.DoubleQuoted : ScalarStyle.Any
            };
        }*/

        public string Value { get; set; }
        public override bool IsNull { get; init; }

        public override bool IsEmpty => string.IsNullOrWhiteSpace(Value);

        private static bool IsNullLiteral(string? value) => value != null && value.Trim().ToLower() is "null" ;

        public override ValueDataNode Copy()
        {
            return new(Value)
            {
                Tag = Tag,
                Start = Start,
                End = End,
                IsNull = IsNull
            };
        }

        public override ValueDataNode? Except(ValueDataNode node)
        {
            return node.Value == Value ? null : Copy();
        }

        public override ValueDataNode PushInheritance(ValueDataNode node)
        {
            return Copy();
        }

        public override bool Equals(object? obj)
        {
            return obj is ValueDataNode node && Equals(node);
        }

        public override int GetHashCode()
        {
            return Value.GetHashCode();
        }

        public override string ToString()
        {
            return Value;
        }

        [Pure]
        public int AsInt()
        {
            return int.Parse(Value, CultureInfo.InvariantCulture);
        }

        [Pure]
        public uint AsUint()
        {
            return uint.Parse(Value, CultureInfo.InvariantCulture);
        }

        [Pure]
        public float AsFloat()
        {
            return float.Parse(Value, CultureInfo.InvariantCulture);
        }

        [Pure]
        public bool AsBool()
        {
            return bool.Parse(Value);
        }

        public bool Equals(ValueDataNode? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Value == other.Value;
        }
}

public readonly struct NodeMark : IEquatable<NodeMark>, IComparable<NodeMark>
{
    public static NodeMark Invalid => new(-1, -1);

    public NodeMark(int line, int column)
    {
        Line = line;
        Column = column;
    }

    /*
    public NodeMark(Mark mark) : this(mark.Line, mark.Column)
    {
    }*/

    public int Line { get; init; }

    public int Column { get; init; }

    public override string ToString()
    {
        return $"Line: {Line}, Col: {Column}";
    }

    public bool Equals(NodeMark other)
    {
        return Line == other.Line && Column == other.Column;
    }

    public override bool Equals(object? obj)
    {
        return obj is NodeMark other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Line, Column);
    }

    public int CompareTo(NodeMark other)
    {
        var lineNum = Line.CompareTo(other.Line);
        return lineNum == 0 ? Column.CompareTo(other.Column) : lineNum;
    }

    // public static implicit operator NodeMark(Mark mark) => new(mark);

    public static bool operator ==(NodeMark left, NodeMark right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(NodeMark left, NodeMark right)
    {
        return !left.Equals(right);
    }

    public static bool operator <(NodeMark? left, NodeMark? right)
    {
        if (left == null || right == null)
        {
            return false;
        }

        return left.Value.CompareTo(right.Value) < 0;
    }

    public static bool operator >(NodeMark? left, NodeMark? right)
    {
        if (left == null || right == null)
        {
            return false;
        }

        return left.Value.CompareTo(right.Value) > 0;
    }
}

public class InvalidNodeTypeException : Exception
{
    public InvalidNodeTypeException()
    {
    }

    public InvalidNodeTypeException(string? message) : base(message)
    {
    }
}

    public static class NullableHelper
    {
        //
        // Since .NET 8, System.Runtime.CompilerServices.NullableAttribute is included in the BCL.
        // Before this, Roslyn emitted a copy of the attribute into every assembly compiled.
        // In the latter case we need to find the type for every assembly that has it.
        // Yeah most of this code can probably be removed now but just for safety I'm keeping it as a fallback path.
        //

        private const int NotAnnotatedNullableFlag = 1;

        private static readonly Type? BclNullableCache;
        private static readonly Type? BclNullableContextCache;

        static NullableHelper()
        {
            BclNullableCache = Type.GetType("System.Runtime.CompilerServices.NullableAttribute");
            BclNullableContextCache = Type.GetType("System.Runtime.CompilerServices.NullableContextAttribute");
        }

        private static readonly Dictionary<Assembly, (Type AttributeType, FieldInfo NullableFlagsField)?>
            _nullableAttributeTypeCache = new();

        private static readonly Dictionary<Assembly, (Type AttributeType, FieldInfo FlagsField)?>
            _nullableContextAttributeTypeCache = new();

        //todo paul remove this shitty hack once serv3 nullable reference is sane again
        public static Type? GetUnderlyingType(this Type type)
        {
            var underlyingType = Nullable.GetUnderlyingType(type);

            if (underlyingType != null) return underlyingType;

            return type.IsValueType ? null : type;
        }

        public static Type EnsureNullableType(this Type type)
        {
            if (type.IsValueType)
            {
                return typeof(Nullable<>).MakeGenericType(type);
            }

            return type;
        }

        public static Type EnsureNotNullableType(this Type type)
        {
            return GetUnderlyingType(type) ?? type;
        }

        /// <summary>
        /// Checks if the field has a nullable annotation [?]
        /// </summary>
        /// <param name="field"></param>
        /// <returns></returns>
        /*
        internal static bool IsMarkedAsNullable(AbstractFieldInfo field)
        {
            //to understand whats going on here, read https://github.com/dotnet/roslyn/blob/main/docs/features/nullable-metadata.md
            if (Nullable.GetUnderlyingType(field.FieldType) != null) return true;

            var flags = GetNullableFlags(field);
            if (flags.Length != 0 && flags[0] != NotAnnotatedNullableFlag) return true;

            if (field.DeclaringType == null || field.FieldType.IsValueType) return false;

            var cflag = GetNullableContextFlag(field.DeclaringType);
            return cflag != NotAnnotatedNullableFlag;
        }
        */

        public static bool IsNullable(this Type type)
        {
            return IsNullable(type, out _);
        }

        public static bool IsNullable(this Type type, [NotNullWhen(true)] out Type? underlyingType)
        {
            underlyingType = GetUnderlyingType(type);

            if (underlyingType == null)
            {
                return false;
            }

            return true;
        }

        /*
        private static byte[] GetNullableFlags(AbstractFieldInfo field)
        {
            lock (_nullableAttributeTypeCache)
            {
                Assembly assembly = field.Module.Assembly;
                if (!_nullableAttributeTypeCache.TryGetValue(assembly, out var assemblyNullableEntry))
                {
                    CacheNullableFieldInfo(assembly);
                }
                assemblyNullableEntry = _nullableAttributeTypeCache[assembly];

                if (assemblyNullableEntry == null)
                {
                    return new byte[]{0};
                }

                if (!field.TryGetAttribute(assemblyNullableEntry.Value.AttributeType, out var nullableAttribute))
                {
                    return new byte[]{1};
                }

                return assemblyNullableEntry.Value.NullableFlagsField.GetValue(nullableAttribute) as byte[] ?? new byte[]{1};
            }
        }
        */

        private static byte GetNullableContextFlag(Type type)
        {
            lock (_nullableContextAttributeTypeCache)
            {
                Assembly assembly = type.Assembly;
                if (!_nullableContextAttributeTypeCache.TryGetValue(assembly, out var assemblyNullableEntry))
                {
                    CacheNullableContextFieldInfo(assembly);
                }
                assemblyNullableEntry = _nullableContextAttributeTypeCache[assembly];

                if (assemblyNullableEntry == null)
                {
                    return 0;
                }

                var nullableAttribute = type.GetCustomAttribute(assemblyNullableEntry.Value.AttributeType);
                if (nullableAttribute == null)
                {
                    return 1;
                }

                return (byte) (assemblyNullableEntry.Value.FlagsField.GetValue(nullableAttribute) ?? 1);
            }
        }

        private static void CacheNullableFieldInfo(Assembly assembly)
        {
            var nullableAttributeType = assembly.GetType("System.Runtime.CompilerServices.NullableAttribute");
            nullableAttributeType ??= BclNullableCache;
            if (nullableAttributeType == null)
            {
                _nullableAttributeTypeCache.Add(assembly, null);
                return;
            }

            var field = nullableAttributeType.GetField("NullableFlags");
            if (field == null)
            {
                _nullableAttributeTypeCache.Add(assembly, null);
                return;
            }

            _nullableAttributeTypeCache.Add(assembly, (nullableAttributeType, field));
        }

        private static void CacheNullableContextFieldInfo(Assembly assembly)
        {
            var nullableContextAttributeType =
                assembly.GetType("System.Runtime.CompilerServices.NullableContextAttribute");
            nullableContextAttributeType ??= BclNullableContextCache;
            if (nullableContextAttributeType == null)
            {
                _nullableContextAttributeTypeCache.Add(assembly, null);
                return;
            }

            var field = nullableContextAttributeType.GetField("Flag");
            if (field == null)
            {
                _nullableContextAttributeTypeCache.Add(assembly, null);
                return;
            }

            _nullableContextAttributeTypeCache.Add(assembly, (nullableContextAttributeType, field));
        }
    }
