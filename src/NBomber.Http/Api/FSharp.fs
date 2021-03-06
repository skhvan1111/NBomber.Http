﻿namespace NBomber.Http.FSharp

open System
open System.Net.Http
open System.Text

open FSharp.Control.Tasks.V2.ContextInsensitive

open NBomber.Contracts
open NBomber.FSharp
open NBomber.Http

module Http =
    
    let createRequest (method: string) (url: string) =        
        { Url = Uri(url)
          Version = Version.Parse("2.0")
          Method = HttpMethod(method)
          Headers = Map.empty
          Body = Unchecked.defaultof<HttpContent>
          Check = fun response -> response.IsSuccessStatusCode }

    let withHeader (name: string) (value: string) (req: HttpRequest) =
        { req with Headers = req.Headers.Add(name, value) }  

    let withHeaders (headers: (string*string) list) (req: HttpRequest) =
        { req with Headers = headers |> Map.ofSeq }

    let withVersion (version: string) (req: HttpRequest) =
        { req with Version = Version.Parse(version) }     

    let withBody (body: HttpContent) (req: HttpRequest) =
        { req with Body = body }

    let withCheck (check: HttpResponseMessage -> bool)  (req: HttpRequest) =
        { req with Check = check }

module HttpStep =

    let private pool = ConnectionPool.create("nbomber.http.pool", (fun () -> lazy new HttpClient()))

    let private createMsg (req: HttpRequest) =
        let msg = new HttpRequestMessage()
        msg.Method <- req.Method
        msg.RequestUri <- req.Url
        msg.Version <- req.Version
        msg.Content <- req.Body
        req.Headers |> Map.iter(fun name value -> msg.Headers.TryAddWithoutValidation(name, value) |> ignore)
        msg
    
    let private getResponseSize (response: HttpResponseMessage) =
        let responseSize =
                response.Content.Headers.ContentLength.GetValueOrDefault(0L)
                |> Convert.ToInt32;
                   
        let headersSize =
            Encoding.UTF8.GetByteCount(response.Content.Headers.ToString())
        
        responseSize + headersSize
    
    let create (name: string) (req: HttpRequest) =
        Step.create(name, pool, fun context -> task { 
            let msg = createMsg(req)
            let! response = context.Connection.Value.SendAsync(msg, context.CancellationToken)
        
            let responseSize = getResponseSize(response)

            match req.Check(response) with
            | true  -> return Response.Ok(response, sizeBytes = responseSize) 
            | false -> return Response.Fail()
        })

    let createFromResponse (name: string) (createReqFn: HttpResponseMessage -> HttpRequest) =    
        Step.create(name, pool, fun context -> task { 
            let previousResponse = context.Data :?> HttpResponseMessage
            let req = createReqFn previousResponse
            
            let msg = createMsg req
            let! response = context.Connection.Value.SendAsync(msg, context.CancellationToken)
        
            let responseSize = getResponseSize(response)
            
            match req.Check(response) with
            | true  -> return Response.Ok(response, sizeBytes = responseSize) 
            | false -> return Response.Fail()
        })