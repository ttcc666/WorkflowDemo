using Approval.Core.Persistence;
using Approval.Forms.Runtime;
using System.Reflection;
using System.Reflection.Emit;

namespace Approval.Core.Tests;

public sealed class FormsBoundaryTests
{
    [Fact]
    public void Forms_ReferencesCoreInternalsOnlyThroughTheFixedBridge()
    {
        var core = typeof(ApprovalDbContextAccessor).Assembly;
        var allowed = typeof(ApprovalFormsBridge);
        var opcodes = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(OpCode)).Select(f => (OpCode)f.GetValue(null)!)
            .ToDictionary(op => unchecked((ushort)op.Value));
        var methodsChecked = 0;
        foreach (var type in typeof(ApprovalFormService).Assembly.GetTypes())
        {
            CheckType(type.BaseType);
            foreach (var contract in type.GetInterfaces()) CheckType(contract);
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) CheckType(field.FieldType);
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)) CheckType(property.PropertyType);
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Cast<MethodBase>()
                .Concat(type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)))
            {
                if (method is MethodInfo info) CheckType(info.ReturnType);
                foreach (var parameter in method.GetParameters()) CheckType(parameter.ParameterType);
                var body = method.GetMethodBody();
                if (body is null) continue;
                foreach (var local in body.LocalVariables) CheckType(local.LocalType);
                var bytes = body.GetILAsByteArray()!;
                methodsChecked++;
                var index = 0;
                while (index < bytes.Length)
                {
                    var key = (ushort)bytes[index++];
                    if (key == 0xfe) key = (ushort)(0xfe00 | bytes[index++]);
                    var op = opcodes[key];
                    if (op.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok)
                    {
                        var target = method.Module.ResolveMember(BitConverter.ToInt32(bytes, index), type.GetGenericArguments(), method.IsGenericMethod ? method.GetGenericArguments() : null)!;
                        CheckType(target is Type referenced ? referenced : target.DeclaringType);
                        if (target.DeclaringType?.Assembly == core && target.DeclaringType != allowed)
                        {
                            var visible = target switch { MethodBase m => m.IsPublic, FieldInfo f => f.IsPublic, _ => true };
                            Assert.True(visible, $"Forms method {type.FullName}.{method.Name} references Core internal member {target}");
                        }
                    }
                    index += op.OperandType switch
                    {
                        OperandType.InlineNone => 0,
                        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                        OperandType.InlineVar => 2,
                        OperandType.InlineI8 or OperandType.InlineR => 8,
                        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(bytes, index),
                        _ => 4
                    };
                }
            }
        }
        Assert.True(methodsChecked > 0, "Forms executable methods must be inspected.");

        void CheckType(Type? type)
        {
            if (type is null) return;
            if (type.HasElementType) CheckType(type.GetElementType());
            if (type.IsGenericType) foreach (var argument in type.GetGenericArguments()) CheckType(argument);
            if (type.Assembly == core && !type.IsVisible)
                Assert.Equal(allowed, type.IsGenericType ? type.GetGenericTypeDefinition() : type);
        }
    }
}
