// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 diazneoones82
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
class InspectReference {
    static int Main(string[] args){
        string file=args[0];AppDomain.CurrentDomain.ReflectionOnlyAssemblyResolve+=delegate(object sender,ResolveEventArgs e){string adjacent=Path.Combine(Path.GetDirectoryName(file),new AssemblyName(e.Name).Name+".dll");return File.Exists(adjacent)?Assembly.ReflectionOnlyLoadFrom(adjacent):Assembly.ReflectionOnlyLoad(e.Name);};
        var assembly=Assembly.ReflectionOnlyLoadFrom(file);
        var map=typeof(OpCodes).GetFields().Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)).ToDictionary(o=>(ushort)o.Value);
        foreach(var type in assembly.GetTypes())foreach(var method in type.GetMethods(BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.Static|BindingFlags.DeclaredOnly).Where(m=>m.Name=="InitializeComponent")){
            var body=method.GetMethodBody();if(body==null)continue;byte[] code=body.GetILAsByteArray();var history=new Queue<string>();
            for(int i=0;i<code.Length;){int offset=i;ushort value=code[i++];if(value==0xfe)value=(ushort)(0xfe00|code[i++]);OpCode op=map[value];object operand="";int length=0;
                switch(op.OperandType){case OperandType.InlineNone:break;case OperandType.ShortInlineI:operand=(sbyte)code[i];length=1;break;case OperandType.ShortInlineVar:operand=code[i];length=1;break;case OperandType.InlineVar:operand=BitConverter.ToUInt16(code,i);length=2;break;case OperandType.InlineMethod:case OperandType.InlineField:case OperandType.InlineType:case OperandType.InlineTok:try{operand=method.Module.ResolveMember(BitConverter.ToInt32(code,i));}catch{operand=BitConverter.ToInt32(code,i);}length=4;break;case OperandType.InlineString:try{operand=method.Module.ResolveString(BitConverter.ToInt32(code,i));}catch{}length=4;break;case OperandType.InlineI:operand=BitConverter.ToInt32(code,i);length=4;break;case OperandType.ShortInlineR:operand=BitConverter.ToSingle(code,i);length=4;break;case OperandType.InlineI8:case OperandType.InlineR:length=8;break;case OperandType.ShortInlineBrTarget:length=1;break;case OperandType.InlineSwitch:length=4+4*BitConverter.ToInt32(code,i);break;default:length=4;break;}
                i+=length;string line=offset.ToString("X4")+" "+op.Name+" "+operand;history.Enqueue(line);while(history.Count>8)history.Dequeue();string name=operand.ToString();if(name.Contains("set_ClientSize")||name.Contains("set_AutoScaleDimensions")||name.Contains("set_FormBorderStyle")){Console.WriteLine(type.FullName+"."+method.Name);foreach(var h in history)Console.WriteLine(h);}
            }
        }return 0;
    }
}
