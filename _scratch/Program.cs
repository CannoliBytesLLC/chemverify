using System.Reflection;
var t = typeof(System.CommandLine.CommandLineConfiguration);
foreach (var c in t.GetConstructors()) { var ps = string.Join(", ", c.GetParameters().Select(p => p.ParameterType.Name + " " + p.Name)); Console.WriteLine("ctor(" + ps + ")"); }
foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly).OrderBy(x => x.Name)) Console.WriteLine(m.MemberType + " " + m.Name + " " + m);