// Copyright 2018-2019 Fabulous contributors. See LICENSE.md for license.
namespace Fabulous.CodeGen.Generator

open System
open System.IO
open Fabulous.CodeGen
open Fabulous.CodeGen.Binder.Models
open Fabulous.CodeGen.Text
open Fabulous.CodeGen.Generator.Models

module CodeGenerator =
    let generateNamespace (namespaceOfGeneratedCode: string) (w: StringWriter) = 
        w.printfn "// Copyright 2018-2019 Fabulous contributors. See LICENSE.md for license."
        w.printfn "namespace %s" namespaceOfGeneratedCode
        w.printfn ""
        w.printfn "#nowarn \"59\" // cast always holds"
        w.printfn "#nowarn \"66\" // cast always holds"
        w.printfn "#nowarn \"67\" // cast always holds"
        w.printfn ""
        w.printfn "open FSharp.Data.Adaptive"
        w.printfn "open Fabulous"
        w.printfn ""
        w

    let adaptType (s: string) =
        if s = "ViewElement" || s.EndsWith("alist") then s
        else sprintf "aval<%s>" s

    let generateAttributes (members: AttributeData array) (w: StringWriter) =
        w.printfn "module ViewAttributes ="
        for m in members do
            let typeName =
                match m.Name with
                | "Created" -> adaptType "(obj -> unit)"
                | _ -> "_"
                
            w.printfn "    let %sAttribKey : AttributeKey<%s> = AttributeKey<%s>(\"%s\")" m.UniqueName typeName typeName m.UniqueName
        w.printfn ""
        w

    let generateBuildMemberArgs (data: BuildData) =
        let memberNewLine = "\n                          "
        let members =
            data.Members
            |> Array.mapi (fun index m -> sprintf "%s%s?%s: %s" (if index = 0 then "" else ",") (if index = 0 then "" else memberNewLine) m.Name (adaptType m.InputType))
            |> String.concat ""

        let immediateMembers =
            data.Members
            |> Array.filter (fun m -> not m.IsInherited)

        let baseMembers =
            match data.BaseName with 
            | None -> ""
            | Some nameOfBaseCreator ->
                data.Members
                |> Array.filter (fun m -> m.IsInherited)
                |> Array.mapi (fun index m -> sprintf "%s%s?%s=%s" (if index = 0 then "" else ", ") (if index > 0 && index % 5 = 0 then memberNewLine else "") m.Name m.Name)
                |> String.concat ""
        members, baseMembers, immediateMembers

    let generateBuildFunction (data: BuildData) (w: StringWriter) =
        let members, baseMembers, immediateMembers = generateBuildMemberArgs data

        w.printfn "    /// Builds the attributes for a %s in the view" data.Name
        w.printfn "    static member inline Build%s(attribCount: int, %s) = " data.Name members

        if immediateMembers.Length > 0 then
            w.printfn ""
            for m in immediateMembers do
                w.printfn "        let attribCount = match %s with Some _ -> attribCount + 1 | None -> attribCount" m.Name
            w.printfn ""

        w.printfn "        let attribBuilder ="
        match data.BaseName with 
        | None ->
            w.printfn "            new AttributesBuilder(attribCount)"
        | Some nameOfBaseCreator ->
            w.printfn "            ViewBuilders.Build%s(attribCount, %s)" nameOfBaseCreator baseMembers

        for m in immediateMembers do
            w.printfn "        match %s with None -> () | Some v -> attribBuilder.Add(ViewAttributes.%sAttribKey, %s%s v) " m.Name m.UniqueName (if not (String.IsNullOrWhiteSpace m.ConvertInputToModel) then "AVal.map " else "") m.ConvertInputToModel 

        w.printfn "        attribBuilder"
        w.printfn ""
        w

    let generateCreateFunction (data: CreateData option) (w: StringWriter) =
        match data with
        | None -> w
        | Some data ->
            w.printfn "    static member Create%s () : %s =" data.Name data.FullName
            
            if data.TypeToInstantiate = data.FullName then
                w.printfn "        new %s()" data.TypeToInstantiate
            else
                w.printfn "        upcast (new %s())" data.TypeToInstantiate
            
            w.printfn ""
            w
        
    let generateUpdateFunction (data: UpdateData) (w: StringWriter) =
        let members, baseMembers, immediateMembers = generateBuildMemberArgs data.BuildData
        w.printfn "    static member inline Update%s (%s) : (AdaptiveToken -> %s -> unit) = " data.Name members data.FullName // data.FullName
        
        match data.BaseName, data.BaseFullName with 
        | Some baseName, Some baseFullName ->
            // Update inherited members
            w.printfn "        let baseUpdate = ViewBuilders.Update%s (%s)"  baseName baseMembers
            w.printfn "        let update = "
            w.printfn "            (fun token (target: %s) ->" data.FullName
            w.printfn "                baseUpdate token (target :> %s))" (baseFullName.Replace("'T", "_"))
            let generateAttachedProperties collectionData =
(*
                if (collectionData.AttachedProperties.Length > 0) then
                    w.printfn "            (fun prevChildOpt newChild targetChild -> "
                    w.printfn "                // Adjust the attached properties"
                    for ap in collectionData.AttachedProperties do
                        let hasApply = not (System.String.IsNullOrWhiteSpace(ap.ConvertModelToValue)) || not (System.String.IsNullOrWhiteSpace(ap.UpdateCode))
                    
                        w.printfn "                let prev%sOpt = match prevChildOpt with ValueNone -> ValueNone | ValueSome prevChild -> prevChild.TryGetAttributeKeyed<%s>(ViewAttributes.%sAttribKey)" ap.UniqueName ap.ModelType ap.UniqueName
                        w.printfn "                let curr%sOpt = newChild.TryGetAttributeKeyed<%s>(ViewAttributes.%sAttribKey)" ap.UniqueName ap.ModelType ap.UniqueName
                    
                        if ap.ModelType = "ViewElement" && not hasApply then
                            w.printfn "                match prev%sOpt, curr%sOpt with" ap.UniqueName ap.UniqueName
                            w.printfn "                // For structured objects, dependsOn on reference equality"
                            w.printfn "                | ValueSome prevValue, ValueSome newValue when identical prevValue newValue -> ()"
                            w.printfn "                | ValueSome prevValue, ValueSome newValue when canReuseView prevValue newValue ->"
                            w.printfn "                    newValue.UpdateIncremental(prevValue, (%s.Get%s(targetChild)))" data.FullName ap.Name
                            w.printfn "                | _, ValueSome newValue ->"
                            w.printfn "                    %s.Set%s(targetChild, (newValue.Create() :?> %s))" data.FullName ap.Name ap.OriginalType
                            w.printfn "                | ValueSome _, ValueNone ->"
                            w.printfn "                    %s.Set%s(targetChild, null)" data.FullName ap.Name
                            w.printfn "                | ValueNone, ValueNone -> ()"
                        
                        elif not (System.String.IsNullOrWhiteSpace(ap.UpdateCode)) then
                            w.printfn "                %s prev%sOpt curr%sOpt targetChild" ap.UniqueName ap.UniqueName ap.UpdateCode
                        
                        else
                            w.printfn "                match prev%sOpt, curr%sOpt with" ap.UniqueName ap.UniqueName
                            w.printfn "                | ValueSome prevChildValue, ValueSome currChildValue when prevChildValue = currChildValue -> ()"
                            w.printfn "                | _, ValueSome currChildValue -> %s.Set%s(targetChild, %s currChildValue)" data.FullName ap.Name ap.ConvertModelToValue
                            w.printfn "                | ValueSome _, ValueNone -> %s.Set%s(targetChild, %s)" data.FullName ap.Name ap.DefaultValue
                            w.printfn "                | _ -> ()"
                
                    w.printfn "                )"
                else
*)        
                    w.printfn "                        (fun _ _ _ -> ())"
        
            w.printfn "        // State held by updater"
            for p in data.Properties do
                let hasApply = not (System.String.IsNullOrWhiteSpace(p.ConvertModelToValue)) || not (System.String.IsNullOrWhiteSpace(p.UpdateCode))
                match p.CollectionData with 
                | Some _collectionData -> ()
                | _ ->
                    if p.ModelType = "ViewElement" || not (System.String.IsNullOrWhiteSpace(p.UpdateCode)) then
                        ()
                    else
                        w.printfn "        let mutable prev%sOpt = ValueNone" p.UniqueName
            
            // Unsubscribe previous event handlers
            if data.Events.Length > 0 then
                w.printfn "        // TODO: Unsubscribe previous event handlers"
                (*
                for e in data.Events do
                    let relatedProperties =
                        e.RelatedProperties
                        |> Array.map (fun p -> sprintf "(identical prev%sOpt curr)" p p)
                        |> Array.fold (fun a b -> a + " && " + b) ""

                    w.printfn "        let shouldUpdate%s = not ((identical prev%sOpt curr)%s)" e.UniqueName e.UniqueName e.UniqueName relatedProperties
                    w.printfn "        if shouldUpdate%s then" e.UniqueName
                    w.printfn "            match prev%sOpt with" e.UniqueName
                    w.printfn "            | ValueSome prevValue -> target.%s.RemoveHandler(prevValue)" e.Name
                    w.printfn "            | ValueNone -> ()"
                    *)

            // Update properties
            if data.Properties.Length > 0 then
                w.printfn "        // Update properties"
                for p in data.Properties do
                    let hasApply = not (System.String.IsNullOrWhiteSpace(p.ConvertModelToValue)) || not (System.String.IsNullOrWhiteSpace(p.UpdateCode))
                    w.printfn "        let update ="
                    w.printfn "            match %s with" p.ShortName
                    w.printfn "            | None -> update"
                    w.printfn "            | Some %s ->" p.ShortName
                    match p.CollectionData with 
                    | Some collectionData when not hasApply ->
                        w.printfn "                let %sUpdater =" p.ShortName
                        w.printfn "                    ViewUpdaters.updateCollectionGeneric %s" p.ShortName
                        w.printfn "                        (fun token (x: ViewElement) -> x.Create(token) :?> %s)" collectionData.ElementType
                        generateAttachedProperties collectionData
                        w.printfn "                        ViewHelpers.canReuseView"
                        w.printfn "                        ViewUpdaters.updateChild"
                    | Some collectionData when hasApply ->
                        w.printfn "                let %sUpdater =" p.ShortName
                        w.printfn "                    %s %s" p.UpdateCode p.ShortName
                        generateAttachedProperties collectionData
                    | _ when p.ModelType = "ViewElement" && not hasApply -> 
                        w.printfn "                let %sUpdater =" p.ShortName
                        w.printfn "                    let mutable created = false" 
                        w.printfn "                    fun token (target: %s) -> " data.FullName
                        w.printfn "                        if created then "
                        w.printfn "                            %s.Update(token, target.%s)" p.ShortName p.Name
                        w.printfn "                        else"
                        w.printfn "                            target.%s <- (%s.Create(token) :?> %s)" p.Name p.ShortName p.OriginalType
                    | _ when not (System.String.IsNullOrWhiteSpace(p.UpdateCode)) ->
                        if not (String.IsNullOrWhiteSpace(p.ConvertModelToValue)) then 
                            w.printfn "                let %s = AVal.map %s %s" p.ShortName p.ConvertModelToValue p.ShortName
                        w.printfn "                let %sUpdater = %s %s" p.ShortName p.UpdateCode p.ShortName
                    | _ -> 
                        w.printfn "                let %sUpdater =" p.ShortName
                        w.printfn "                    let mutable prevOpt = ValueNone" 
                        w.printfn "                    fun token (target: %s) -> " data.FullName
                        w.printfn "                        let curr = %s.GetValue(token)" p.ShortName
                        w.printfn "                        match prev%sOpt with" p.UniqueName
                        w.printfn "                        | ValueSome prev when prev = curr -> ()"
                        w.printfn "                        | _ -> target.%s <- %s curr" p.Name p.ConvertModelToValue
                        w.printfn "                        prev%sOpt <- ValueSome curr" p.UniqueName
                    w.printfn "                (fun token (target: %s) -> " data.FullName
                    w.printfn "                    update token target"
                    match p.CollectionData with 
                    | Some _collectionData when not hasApply ->
                        w.printfn "                    %sUpdater token target.%s)" p.ShortName p.Name 
                    | Some _collectionData ->
                        w.printfn "                    %sUpdater token target)" p.ShortName
                    | _ ->
                        w.printfn "                    %sUpdater token target)" p.ShortName

            // Subscribe event handlers
            if data.Events.Length > 0 then
                w.printfn "        // TODO: Subscribe new event handlers"
(*
                for e in data.Events do
                    w.printfn "        if shouldUpdate%s then" e.UniqueName
                    w.printfn "            match curr with" e.UniqueName
                    w.printfn "            | ValueSome currValue -> target.%s.AddHandler(currValue)" e.Name
                    w.printfn "            | ValueNone -> ()"
*)
            w.printfn "        update"
        | _ -> 
            w.printfn "        (fun _ _ -> ())"
                
        w.printfn ""
        w

    let memberArgumentType name inputType fullName =
        match name with
        | "created" -> sprintf "(%s -> unit)" fullName
        | "ref" ->     sprintf "ViewRef<%s>" fullName
        | _ -> inputType
        |> adaptType


    let generateConstruct (data: ConstructData option) (w: StringWriter) =
        match data with
        | None -> ()
        | Some data ->
            let memberNewLine = "\n                                  " + String.replicate data.Name.Length " " + " "
            let space = "\n                               "
            let membersForConstructor =
                data.Members
                |> Array.mapi (fun i m ->
                    let commaSpace = if i = 0 then "" else "," + memberNewLine
                    sprintf "%s?%s: %s" commaSpace m.Name (memberArgumentType m.Name m.InputType data.FullName))
                |> String.concat ""

            let membersForBuild =
                data.Members
                |> Array.map (fun m ->
                    let value = 
                        match m.Name with
                        | "created" -> sprintf "(%s |> Option.map (AVal.map (fun createdFunc -> (unbox<%s> >> createdFunc))))" m.Name data.FullName
                        | "ref" ->     sprintf "(%s |> Option.map (AVal.map (fun (ref: ViewRef<%s>) -> ref.Unbox)))" m.Name data.FullName
                        | _ ->         m.Name
                    sprintf ",%s?%s=%s" space m.Name value)
                |> String.concat ""

            w.printfn "    static member inline Construct%s(%s) = " data.Name membersForConstructor
            w.printfn ""
            w.printfn "        let attribBuilder = ViewBuilders.Build%s(0%s)" data.Name membersForBuild
            w.printfn ""
            w.printfn "        let update = ViewBuilders.Update%s(%s)" data.Name membersForBuild.[1..]
            w.printfn "        ViewElement.Create<%s>(ViewBuilders.Create%s, update, attribBuilder.Close())" data.FullName data.Name
            w.printfn ""

    let generateBuilders (data: BuilderData array) (w: StringWriter) =
        w.printfn "type ViewBuilders() ="
        for typ in data do
            w
            |> generateBuildFunction typ.Build
            |> generateCreateFunction typ.Create
            |> generateUpdateFunction typ.Update
            |> generateConstruct typ.Construct
        w

    let generateViewers (data: ViewerData array) (w: StringWriter) =
        for typ in data do
            let genericConstraint =
                match typ.GenericConstraint with
                | None -> ""
                | Some constr -> sprintf "<%s>" constr
            
            w.printfn "/// Viewer that allows to read the properties of a ViewElement representing a %s" typ.Name
            w.printfn "type %s%s(element: ViewElement) =" typ.ViewerName genericConstraint

            match typ.InheritedViewerName with
            | None -> ()
            | Some inheritedViewerName ->
                let inheritedGenericConstraint =
                    match typ.InheritedGenericConstraint with
                    | None -> ""
                    | Some constr -> sprintf "<%s>" constr
                
                w.printfn "    inherit %s%s(element)" inheritedViewerName inheritedGenericConstraint

            w.printfn "    do if not ((typeof<%s>).IsAssignableFrom(element.TargetType)) then failwithf \"A ViewElement assignable to type '%s' is expected, but '%%s' was provided.\" element.TargetType.FullName" typ.FullName typ.FullName
            for m in typ.Members do
                match m.Name with
                | "Created" | "Ref" -> ()
                | _ ->
                    w.printfn "    /// Get the value of the %s member" m.Name
                    w.printfn "    member this.%s = element.GetAttributeKeyed(ViewAttributes.%sAttribKey)" m.Name m.UniqueName
            w.printfn ""
        w

    let generateConstructors (data: ConstructorData array) (w: StringWriter) =
        w.printfn "[<AbstractClass; Sealed>]"
        w.printfn "type View private () ="

        for d in data do
            let memberNewLine = "\n                         " + String.replicate d.Name.Length " " + " "
            let space = "\n                               "
            let membersForConstructor =
                d.Members
                |> Array.mapi (fun i m ->
                    let commaSpace = if i = 0 then "" else "," + memberNewLine
                    sprintf "%s?%s: %s" commaSpace m.Name (memberArgumentType m.Name m.InputType d.FullName))
                |> String.concat ""
            let membersForConstruct =
                d.Members
                |> Array.mapi (fun i m ->
                    let commaSpace = if i = 0 then "" else "," + space
                    sprintf "%s?%s=%s" commaSpace m.Name m.Name)
                |> String.concat ""

            w.printfn "    /// Describes a %s in the view" d.Name
            w.printfn "    static member inline %s(%s) =" d.Name membersForConstructor
            w.printfn ""
            w.printfn "        ViewBuilders.Construct%s(%s)" d.Name membersForConstruct
            w.printfn ""
        w.printfn ""
        w

    let generateViewExtensions (data: ViewExtensionsData array) (w: StringWriter) : StringWriter =
        let newLine = "\n                             "

        w.printfn "[<AutoOpen>]"
        w.printfn "module ViewElementExtensions = "
        w.printfn ""
        w.printfn "    type ViewElement with"

        for m in data do
            match m.UniqueName with
            | "Created" | "Ref" -> ()
            | _ ->
                w.printfn ""
                w.printfn "        /// Adjusts the %s property in the visual element" m.UniqueName
                w.printfn "        member x.%s(value: %s) = x.WithAttribute(ViewAttributes.%sAttribKey, %s%s value)" m.UniqueName (adaptType m.InputType) m.UniqueName (if not (String.IsNullOrWhiteSpace m.ConvertInputToModel) then "AVal.map " else "") m.ConvertInputToModel

        let members =
            data
            |> Array.filter (fun m -> m.UniqueName <> "Created" && m.UniqueName <> "Ref")
            |> Array.mapi (fun index m -> sprintf "%s%s?%s: %s" (if index > 0 then ", " else "") (if index > 0 && index % 5 = 0 then newLine else "") m.LowerUniqueName (adaptType m.InputType))
            |> String.concat ""

        w.printfn ""
        w.printfn "        member inline x.With(%s) =" members
        for m in data do
            match m.UniqueName with
            | "Created" | "Ref" -> ()
            | _ -> w.printfn "            let x = match %s with None -> x | Some opt -> x.%s(opt)" m.LowerUniqueName m.UniqueName
        w.printfn "            x"
        w.printfn ""

        for m in data do
            match m.UniqueName with
            | "Created" | "Ref" -> ()
            | _ ->
                w.printfn "    /// Adjusts the %s property in the visual element" m.UniqueName
                w.printfn "    let %s (value: %s) (x: ViewElement) = x.%s(value)" m.LowerUniqueName (adaptType m.InputType) m.UniqueName
        w
        
    let generate data =
        let toString (w: StringWriter) = w.ToString()
        use writer = new StringWriter()
        
        writer
        // adaptive 
        |> generateNamespace data.Namespace
        |> generateAttributes data.Attributes
        |> generateBuilders data.Builders
        |> generateViewers data.Viewers
        |> generateConstructors data.Constructors
        |> generateViewExtensions data.ViewExtensions
        |> toString

    let generateCode
        (prepareData: BoundModel -> GeneratorData)
        (generate: GeneratorData -> string)
        (bindings: BoundModel) : WorkflowResult<string> =
        
        bindings
        |> prepareData
        |> generate
        |> WorkflowResult.ok
