module EventGrid.Web.Program

open Giraffe
open Microsoft.AspNetCore
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.Azure.EventGrid
open Microsoft.Azure.EventGrid.Models
open Microsoft.Extensions.DependencyInjection
open Newtonsoft.Json
open Serilog
open Serilog.Events
open System
open System.Collections.Generic

let messages = new List<string>()

let formatMessages() =
    let m = String.Join("<br/>", messages)
    sprintf "messages received:<br/>%s" m
    
let eventType = "sendMessage"

let messageUpdateHandler: HttpHandler =
    let eventGridSubscriber = new EventGridSubscriber()
    eventGridSubscriber.AddOrUpdateCustomEventMapping(eventType, typeof<string>)
    let respondToSubscriptionValidationEvent (request: EventGridEvent) =
        let response = new SubscriptionValidationResponse()
        response.ValidationResponse <- (request.Data :?> SubscriptionValidationEventData).ValidationCode
        JsonConvert.SerializeObject(response)
        
    let processMessageEvent (request: EventGridEvent) =
        let message = request.Data :?> string
        messages.Add message
        request

    handleContext(
        fun ctx ->
            use streamReader = new StreamReader(ctx.Request.Body)
            let bodyText = streamReader.ReadToEnd()
            Log.Information(bodyText)
            
            let events =
                match bodyText with
                | "" | null -> Array.empty<EventGridEvent>
                | t -> eventGridSubscriber.DeserializeEventGridEvents(bodyText)

            let subscriptionResponse =
                events
                // check for subscription requests
                |> Seq.filter (fun event -> event.Data.GetType() = typeof<SubscriptionValidationEventData>)
                |> Seq.map respondToSubscriptionValidationEvent
                |> Seq.tryHead

            let isSendMessage (e: EventGridEvent) = e.EventType = eventType

            // process messages
            let messageEvents =
                events
                |> Seq.filter isSendMessage
                |> Seq.map processMessageEvent

            match Seq.length messageEvents with 
            | 0 -> subscriptionResponse |> Option.defaultValue ("No subscription detected")
            | _ -> formatMessages()
            |> ctx.WriteTextAsync
    )

let getMessages : HttpHandler =
    handleContext(
        fun ctx ->
             formatMessages() |> ctx.WriteHtmlStringAsync
    )

let webApp =
    choose [
        GET >=>
            choose [
                route "/api/updates"    >=> messageUpdateHandler
                route "/messages"       >=> getMessages
                route "/ping"           >=> text "pong"
                route "/"               >=> text "index"
            ]
        POST >=>
            choose [
                route "/api/updates"    >=> messageUpdateHandler
            ]
    ]

let configureApp (app : IApplicationBuilder) =
    // Add Giraffe to the ASP.NET Core pipeline
    app.UseGiraffe webApp

let configureServices (services : IServiceCollection) =
    let log = new LoggerConfiguration()
    let logger =
        log.MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Debug()
            .Enrich.FromLogContext()
            .WriteTo.ApplicationInsightsEvents("04516d51-4df5-4364-ab1e-b9ed24fb9747")
            .CreateLogger()
    Log.Logger <- logger;
    // Add Giraffe dependencies
    services.AddGiraffe() |> ignore

[<EntryPoint>]
let main _ =
    WebHost.CreateDefaultBuilder()
        .UseKestrel()
        .Configure(Action<IApplicationBuilder> configureApp)
        .ConfigureServices(configureServices)
        .UseSerilog()
        .Build()
        .Run()
    0