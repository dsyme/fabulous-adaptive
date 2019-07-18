namespace Fabulous.Generator

open System
open System.IO
open System.Runtime.Loader

module Reflection =
    type ReflectedAttachedProperty = {
        Name: string
        Type: string
        DefaultValue: string
    }
    
    let loadAllAssemblies (paths: seq<string>) =
        let toFullPath p = Path.Combine(Environment.CurrentDirectory, p)
        
        paths
        |> Seq.map (toFullPath >> AssemblyLoadContext.Default.LoadFromAssemblyPath)
        |> Seq.toArray
    
    let tryGetProperty (assembly: System.Reflection.Assembly) typeName propertyName =
        match assembly.GetType(typeName) with
        | null -> None
        | ``type`` ->
            if ``type``.ContainsGenericParameters then
                None
            else
                match ``type``.GetField(propertyName) with
                | null -> None
                | propertyInfo ->
                    match  propertyInfo.GetValue(null) with
                    | null -> None
                    | property ->
                        let propertyType = property.GetType()
                        let returnType = propertyType.GetProperty("ReturnType").GetValue(property) :?> Type
                        Some
                            { Name = propertyType.GetProperty("PropertyName").GetValue(property) :?> string
                              Type = returnType.FullName
                              DefaultValue =
                                match propertyType.GetProperty("DefaultValue").GetValue(property) with
                                | null -> "null"
                                | value ->
                                    match returnType.IsEnum with
                                    | false -> value.ToString()
                                    | true -> sprintf "%s.%s" returnType.FullName (value.ToString()) }