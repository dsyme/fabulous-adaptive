﻿// Copyright 2018-2019 Fabulous contributors. See LICENSE.md for license.
namespace CounterApp

open Fabulous
open Fabulous.XamarinForms
open Fabulous.XamarinForms.LiveUpdate
open Xamarin.Forms
open FSharp.Data.Adaptive
open System.Diagnostics

module App = 
    type Model = 
      { Count : int
        Step : int
        TimerOn: bool }

    [<RequireQualifiedAccess>]
    type AdaptiveModel = 
      { Count : cval<int>
        Step : cval<int>
        TimerOn: cval<bool>  }

    let ainit (model: Model) : AdaptiveModel = 
        { Count = cval model.Count
          Step = cval model.Step
          TimerOn = cval model.TimerOn  }

    let adelta (model: Model) (amodel: AdaptiveModel) =
        transact (fun () -> 
            if model.Count <> amodel.Count.Value then 
                amodel.Count.Value <- model.Count
            if model.Step <> amodel.Step.Value then 
                amodel.Step.Value <- model.Step
            if model.TimerOn <> amodel.TimerOn.Value then 
                amodel.TimerOn.Value <- model.TimerOn)

    type Msg = 
        | Increment 
        | Decrement 
        | Reset
        | SetStep of int
        | TimerToggled of bool
        | TimedTick

    type CmdMsg =
        | TickTimer

    let timerCmd () =
        async { do! Async.Sleep 200
                return TimedTick }
        |> Cmd.ofAsyncMsg

    let mapCmdMsgToCmd cmdMsg =
        match cmdMsg with
        | TickTimer -> timerCmd()

    let initialModel = { Count = 0; Step = 1; TimerOn = false }

    let init () = initialModel, []

    let update msg (model: Model) =
        match msg with
        | Increment -> { model with Count = model.Count + model.Step }, []
        | Decrement -> { model with Count = model.Count - model.Step }, []
        | Reset -> init ()
        | SetStep n -> { model with Step = n }, []
        | TimerToggled on -> { model with TimerOn = on }, (if on then [ TickTimer ] else [])
        | TimedTick -> if model.TimerOn then { model with Count = model.Count + model.Step }, [ TickTimer ] else model, [] 

    let view (model: AdaptiveModel) dispatch =  
        View.ContentPage(
          content=View.StackLayout(padding = c (Thickness 30.0), verticalOptions = c LayoutOptions.Center,
            children = cs [
              View.Label(automationId = c "CountLabel", 
                  text = (model.Count |> AVal.map (sprintf "%d")),
                  horizontalOptions = c LayoutOptions.Center, 
                  width = c 200.0, 
                  horizontalTextAlignment = c TextAlignment.Center)
              View.Button(automationId = c "IncrementButton",
                  text = c "Increment",
                  command= c (fun () -> dispatch Increment))
              View.Button(automationId = c "DecrementButton",
                  text = c "Decrement",
                  command= c (fun () -> dispatch Decrement)) 
              View.StackLayout(padding = c (Thickness 20.0), 
                  orientation = c StackOrientation.Horizontal,
                  horizontalOptions = c LayoutOptions.Center,
                  children = cs [ 
                      View.Label(text = c "Timer")
                      View.Switch(automationId = c "TimerSwitch",
                          isToggled = model.TimerOn, 
                          toggled = c (fun on -> dispatch (TimerToggled on.Value))) ])
              View.Slider(automationId = c "StepSlider", 
                  minimumMaximum = c (0.0, 10.0), 
                  value = (model.Step |> AVal.map double),
                  valueChanged = c (fun args -> dispatch (SetStep (int (args.NewValue + 0.5)))))
              View.Label(automationId = c "StepSizeLabel",
                  text= (model.Step |> AVal.map (sprintf "Step size: %d")),
                  horizontalOptions = c LayoutOptions.Center)
              View.Button(text = c "Reset",
                  horizontalOptions = c LayoutOptions.Center,
                  command = c (fun () -> dispatch Reset),
                  commandCanExecute = 
                      ((model.Step, model.Count, model.TimerOn) |||> AVal.map3 (fun step count timerOn -> 
                          step <> initialModel.Step || 
                          count <> initialModel.Count || 
                          timerOn <> initialModel.TimerOn)))
            ]))
             
    let program = 
        Program.mkProgramWithCmdMsg init update ainit adelta view mapCmdMsgToCmd

type CounterApp () as app = 
    inherit Application ()

    let runner =
        App.program
        |> Program.withConsoleTrace
        |> XamarinFormsProgram.run app

#if DEBUG
    // Run LiveUpdate using: 
    //    
    do runner.EnableLiveUpdate ()
#endif


#if SAVE_MODEL_WITH_JSON
    let modelId = "model"
    override __.OnSleep() = 

        let json = Newtonsoft.Json.JsonConvert.SerializeObject(runner.CurrentModel)
        Debug.WriteLine("OnSleep: saving model into app.Properties, json = {0}", json)

        app.Properties.[modelId] <- json

    override __.OnResume() = 
        Debug.WriteLine "OnResume: checking for model in app.Properties"
        try 
            match app.Properties.TryGetValue modelId with
            | true, (:? string as json) -> 

                Debug.WriteLine("OnResume: restoring model from app.Properties, json = {0}", json)
                let model = Newtonsoft.Json.JsonConvert.DeserializeObject<App.Model>(json)

                Debug.WriteLine("OnResume: restoring model from app.Properties, model = {0}", (sprintf "%0A" model))
                runner.SetCurrentModel (model, Cmd.none)

            | _ -> ()
        with ex ->
            runner.OnError ("Error while restoring model found in app.Properties", ex)

    override this.OnStart() = 
        Debug.WriteLine "OnStart: using same logic as OnResume()"
        this.OnResume()

#endif
