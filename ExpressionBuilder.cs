using System.Linq.Expressions;
using System.Reflection;

namespace ExpressionTreeJitContentionTest;

public sealed class ExpressionBuilder
{
    private readonly Random _random;

    public ExpressionBuilder(int seed)
    {
        _random = new Random(seed);
    }

    public Expression<SerializeDelegateSignature> CreateExpression()
    {
        // This is just some nonsense code to get some inline candidates.

        var paramObject = Expression.Parameter(typeof(object), "object");
        var paramContext = Expression.Parameter(typeof(ISerializationContext), "context");
        var paramAlwaysWrite = Expression.Parameter(typeof(bool), "alwaysWrite");

        var serManager = Expression.Constant(new SerializationManager());
        var fieldCount = _random.Next(5, 20);

        var expressions = new List<Expression>();

        for (var i = 0; i < fieldCount; i++)
        {
            var paramValue = Expression.Variable(typeof(int), "value");

            var nodeVariable = Expression.Variable(typeof(DataNode), "node");

            var call = Expression.Call(
                serManager,
                "WriteValue",
                [typeof(int)],
                paramValue,
                paramAlwaysWrite,
                paramContext,
                Expression.Constant(true));

            expressions.Add(Expression.Block(
                [paramValue, nodeVariable],
                [
                    Expression.Assign(paramValue, AccessExpression(i, paramObject)),
                    Expression.Assign(nodeVariable, call),
                ])
            );
        }

        return Expression.Lambda<SerializeDelegateSignature>(
            Expression.Block(expressions),
            paramObject,
            paramContext,
            paramAlwaysWrite);
    }

    private Expression AccessExpression(int i, Expression obj)
    {
        return Expression.Invoke(Expression.Constant(AccessorCached), obj);
    }

    private static readonly Func<object, int> AccessorCached = CreateAccessor();

    private static Func<object, int> CreateAccessor()
    {
        var paramObject = Expression.Parameter(typeof(object), "object");
        var accessor = Expression.Lambda<Func<object, int>>(Expression.Constant(5), paramObject).Compile();
        return accessor;
    }
}