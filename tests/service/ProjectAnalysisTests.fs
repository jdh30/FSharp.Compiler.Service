﻿module FSharp.Compiler.Service.Tests.ProjectAnalysisTests

#if INTERACTIVE
#r "../../bin/FSharp.Compiler.Service.dll"
#r "../../packages/NUnit.2.6.3/lib/nunit.framework.dll"
#load "FsUnit.fs"
#load "Common.fs"
#endif

open Microsoft.FSharp.Compiler
open Microsoft.FSharp.Compiler.SourceCodeServices

open NUnit.Framework
open FsUnit
open System
open System.IO

open System
open System.Collections.Generic
open Microsoft.FSharp.Compiler.SourceCodeServices

// Create an interactive checker instance 
let checker = InteractiveChecker.Create()

module Inputs = 
    open System.IO

    let base1 = Path.GetTempFileName()
    let fileName1 = Path.ChangeExtension(base1, ".fs")
    let base2 = Path.GetTempFileName()
    let fileName2 = Path.ChangeExtension(base2, ".fs")
    let dllName = Path.ChangeExtension(base2, ".dll")
    let projFileName = Path.ChangeExtension(base2, ".fsproj")
    let fileSource1 = """
module M

type C() = 
    member x.P = 1

let xxx = 3 + 4
let fff () = xxx + xxx
    """
    File.WriteAllText(fileName1, fileSource1)

    let fileSource2 = """
module N

open M

type D1() = 
    member x.SomeProperty = M.xxx

type D2() = 
    member x.SomeProperty = M.fff() + D1().P

// Generate a warning
let y2 = match 1 with 1 -> M.xxx
    """
    File.WriteAllText(fileName2, fileSource2)



let projectOptions = 
    checker.GetProjectOptionsFromCommandLineArgs
       (Inputs.projFileName,
        [| yield "--simpleresolution" 
           yield "--simpleresolution" 
           yield "--noframework" 
           yield "--debug:full" 
           yield "--define:DEBUG" 
           yield "--optimize-" 
           yield "--out:" + Inputs.dllName
           yield "--doc:test.xml" 
           yield "--warn:3" 
           yield "--fullpaths" 
           yield "--flaterrors" 
           yield "--target:library" 
           yield Inputs.fileName1
           yield Inputs.fileName2
           let references = 
             [ @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0\mscorlib.dll" 
               @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0\System.dll" 
               @"C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.0\System.Core.dll" 
               @"C:\Program Files (x86)\Reference Assemblies\Microsoft\FSharp\.NETFramework\v4.0\4.3.0.0\FSharp.Core.dll"]  
           for r in references do
                 yield "-r:" + r |])


[<Test>]
let ``Test project whole project errors`` () = 
  if System.Environment.OSVersion.Platform = System.PlatformID.Win32NT then // file references only valid on Windows 
    let wholeProjectResults = checker.ParseAndCheckProject(projectOptions) |> Async.RunSynchronously
    wholeProjectResults .Errors.Length |> shouldEqual 2
    wholeProjectResults.Errors.[1].Message.Contains("Incomplete pattern matches on this expression") |> shouldEqual true // yes it does

    wholeProjectResults.Errors.[0].StartLine |> shouldEqual 9
    wholeProjectResults.Errors.[0].EndLine |> shouldEqual 9
    wholeProjectResults.Errors.[0].StartColumn |> shouldEqual 43
    wholeProjectResults.Errors.[0].EndColumn |> shouldEqual 44

[<Test>]
let ``Test project basic`` () = 
  if System.Environment.OSVersion.Platform = System.PlatformID.Win32NT then // file references only valid on Windows 

    let wholeProjectResults = checker.ParseAndCheckProject(projectOptions) |> Async.RunSynchronously
    set [ for x in wholeProjectResults.AssemblySignature.Entities -> x.DisplayName ] |> shouldEqual (set ["N"; "M"])
    [ for x in wholeProjectResults.AssemblySignature.Entities.[0].NestedEntities -> x.DisplayName ] |> shouldEqual ["D1"; "D2"]
    [ for x in wholeProjectResults.AssemblySignature.Entities.[1].NestedEntities -> x.DisplayName ] |> shouldEqual ["C"]
    [ for x in wholeProjectResults.AssemblySignature.Entities.[0].MembersFunctionsAndValues -> x.DisplayName ] |> shouldEqual ["y2"]

let rec allSymbolsInEntities (entities: IList<FSharpEntity>) = 
    [ for e in entities do 
          yield (e :> FSharpSymbol) 
          for x in e.MembersFunctionsAndValues do
             yield (x :> FSharpSymbol)
          for x in e.UnionCases do
             yield (x :> FSharpSymbol)
          for x in e.RecordFields do
             yield (x :> FSharpSymbol)
          yield! allSymbolsInEntities e.NestedEntities ]


[<Test>]
let ``Test project all symbols`` () = 

    let wholeProjectResults = checker.ParseAndCheckProject(projectOptions) |> Async.RunSynchronously
    let allSymbols = allSymbolsInEntities wholeProjectResults.AssemblySignature.Entities
    [ for x in allSymbols -> x.ToString() ] 
      |> shouldEqual 
         [ "N"; "val y2"; "D1"; "member ( .ctor )"; "member SomeProperty"; "D2"; 
           "member ( .ctor )"; "member SomeProperty"; "M"; "val xxx"; "val fff"; "C"; "member ( .ctor )"; "member P" ]

[<Test>]
let ``Test project xxx symbols`` () = 

  if System.Environment.OSVersion.Platform = System.PlatformID.Win32NT then // file references only valid on Windows 
    let wholeProjectResults = checker.ParseAndCheckProject(projectOptions) |> Async.RunSynchronously
    let backgroundParseResults1, backgroundTypedParse1 = 
        checker.GetBackgroundCheckResultsForFileInProject(Inputs.fileName1, projectOptions) 
        |> Async.RunSynchronously

    let xSymbol = backgroundTypedParse1.GetSymbolAtLocation(8,9,"",["xxx"]).Value
    xSymbol.ToString() |> shouldEqual "val xxx"

    let usesOfXSymbol = wholeProjectResults.GetUsesOfSymbol(xSymbol)
    usesOfXSymbol |> shouldEqual [|(Inputs.fileName1, ((6, 4), (6, 7)));
                                   (Inputs.fileName1, ((7, 13), (7, 16)));
                                   (Inputs.fileName1, ((7, 19), (7, 22)));
                                   (Inputs.fileName2, ((6, 28), (6, 33)));
                                   (Inputs.fileName2, ((12, 27), (12, 32)))|]

(**
You can iterate all the defined symbols in the inferred signature and find where they are used:
*)
[<Test>]
let ``Test project all uses of all symbols`` () = 
  if System.Environment.OSVersion.Platform = System.PlatformID.Win32NT then // file references only valid on Windows 
    let wholeProjectResults = checker.ParseAndCheckProject(projectOptions) |> Async.RunSynchronously
    let allSymbols = allSymbolsInEntities wholeProjectResults.AssemblySignature.Entities
    let allUsesOfAllSymbols = 
        [ for s in allSymbols do 
             yield s.ToString(), wholeProjectResults.GetUsesOfSymbol(s) ]
    (allUsesOfAllSymbols =
         
             [("N",
                [|(Inputs.fileName2, ((1, 7), (1, 8)))|]);
               ("val y2",
                [|(Inputs.fileName2, ((12, 4), (12, 6)))|]);
               ("D1",
                [|(Inputs.fileName2, ((5, 5), (5, 7)));
                  (Inputs.fileName2, ((9, 38), (9, 40)))|]);
               ("member ( .ctor )",
                [|(Inputs.fileName2, ((5, 5), (5, 7)))|]);
               ("member SomeProperty",
                [|(Inputs.fileName2, ((6, 13), (6, 25)))|]);
               ("D2",
                [|(Inputs.fileName2, ((8, 5), (8, 7)))|]);
               ("member ( .ctor )",
                [|(Inputs.fileName2, ((8, 5), (8, 7)))|]);
               ("member SomeProperty",
                [|(Inputs.fileName2, ((9, 13), (9, 25)))|]);
               ("M",
                [|(Inputs.fileName1, ((1, 7), (1, 8)));
                  (Inputs.fileName2, ((6, 28), (6, 29)));
                  (Inputs.fileName2, ((9, 28), (9, 29)));
                  (Inputs.fileName2, ((12, 27), (12, 28)))|]);
               ("val xxx",
                [|(Inputs.fileName1, ((6, 4), (6, 7)));
                  (Inputs.fileName1, ((7, 13), (7, 16)));
                  (Inputs.fileName1, ((7, 19), (7, 22)));
                  (Inputs.fileName2, ((6, 28), (6, 33)));
                  (Inputs.fileName2, ((12, 27), (12, 32)))|]);
               ("val fff",
                [|(Inputs.fileName1, ((7, 4), (7, 7)));
                  (Inputs.fileName2, ((9, 28), (9, 33)))|]);
               ("C",
                [|(Inputs.fileName1, ((3, 5), (3, 6)))|]);
               ("member ( .ctor )",
                [|(Inputs.fileName1, ((3, 5), (3, 6)))|]);
               ("member P",
                [|(Inputs.fileName1, ((4, 13), (4, 14)))|])])
        |> shouldEqual true

[<Test>]
let ``Test file explicit parse symbols`` () = 
  if System.Environment.OSVersion.Platform = System.PlatformID.Win32NT then // file references only valid on Windows 

    let wholeProjectResults = checker.ParseAndCheckProject(projectOptions) |> Async.RunSynchronously
    let parseResults1 = checker.ParseFileInProject(Inputs.fileName1, Inputs.fileSource1, projectOptions) 
    let parseResults2 = checker.ParseFileInProject(Inputs.fileName2, Inputs.fileSource2, projectOptions) 

    let checkResults1 = 
        checker.CheckFileInProject(parseResults1, Inputs.fileName1, 0, Inputs.fileSource1, projectOptions) 
        |> Async.RunSynchronously
        |> function CheckFileAnswer.Succeeded x ->  x | _ -> failwith "unexpected aborted"

    let checkResults2 = 
        checker.CheckFileInProject(parseResults2, Inputs.fileName2, 0, Inputs.fileSource2, projectOptions)
        |> Async.RunSynchronously
        |> function CheckFileAnswer.Succeeded x ->  x | _ -> failwith "unexpected aborted"

    let xSymbol2 = checkResults1.GetSymbolAtLocation(8,9,"",["xxx"]).Value
    let usesOfXSymbol2 = wholeProjectResults.GetUsesOfSymbol(xSymbol2)

    usesOfXSymbol2
         |> shouldEqual [|(Inputs.fileName1, ((6, 4), (6, 7)));
                          (Inputs.fileName1, ((7, 13), (7, 16)));
                          (Inputs.fileName1, ((7, 19), (7, 22)));
                          (Inputs.fileName2, ((6, 28), (6, 33)));
                          (Inputs.fileName2, ((12, 27), (12, 32)))|]

