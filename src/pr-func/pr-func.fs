namespace pr.func


open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Http
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json.Linq


module PrecompiledHttp =
  open System
  open System.Net.Http
  open System.Net
  open System.Net.Http.Headers
  open System.Text


  let content = sprintf """{
  "state": "%s",
  "description" : "pull request WIP status",
  "context": "pr/wip"
}"""

  let contentDnm = sprintf """{
  "state": "%s",
  "description" : "DO NOT MERGE commits in tree",
  "context": "pr/do-not-merge"
}"""

  let run(req: HttpRequestMessage, log: TraceWriter) =
    let runner = async {
        let! txt = req.Content.ReadAsStringAsync() |> Async.AwaitTask
        let jobj = JObject.Parse txt

        let get key (token : JToken) =
            token.[key] |> Option.ofObj
        let value (token: JToken) =
            token.Value<_>()
        let array (token: JToken) =
            match token with
            | :? JArray as a -> Some a
            | _ -> None
        let pr = jobj |> get "pull_request"
        let title: string option =  pr |> Option.bind (get "title") |> Option.map value
        let stats: string option = pr |> Option.bind (get "statuses_url")  |> Option.map value

        let commits: string option = pr |> Option.bind (get "commits_url") |> Option.map value

        let auth = Environment.GetEnvironmentVariable("GITHUB_CREDENTIALS")

        match (title, stats) with
        | (Some t, Some s) ->
          let isWip = t.StartsWith("WIP:") || t.StartsWith("[WIP]")
          let body = match isWip with
                      | true -> content "error"
                      | false -> content "success"

          use client = new HttpClient()

          client.DefaultRequestHeaders.UserAgent.Add(
              ProductInfoHeaderValue("username", "version"))

          // Add the GITHUB_CREDENTIALS as an app setting, Value for the app setting is a base64 encoded string in the following format
          // "Username:Password" or "Username:PersonalAccessToken"
          // Please follow the link https://developer.github.com/v3/oauth/ to get more information on GitHub authentication
          client.DefaultRequestHeaders.Authorization <-
              AuthenticationHeaderValue("Basic", auth)
          use content =
              new StringContent(body, Encoding.UTF8, "application/json")
          let! res =  client.PostAsync(s, content) |> Async.AwaitTask
          log.Info (sprintf "got response code: %A" res.StatusCode)


          match commits with
            | Some url ->
              let! res = client.GetAsync(url) |> Async.AwaitTask
              match res.StatusCode with
              | HttpStatusCode.OK ->
                let! content = res.Content.ReadAsStringAsync() |> Async.AwaitTask
                let resObj = JArray.Parse content

                let valid (message: string) =
                    let lower = message.ToLower()
                    not (lower.Contains("do not merge"))

                let messages: string seq =
                    resObj
                    |> Seq.choose(get "commit")
                    |> Seq.choose(get "message")
                    |> Seq.map value

                log.Info(sprintf "got messages: %A" messages)
                let allValid = messages |> Seq.forall valid
                let body =
                    match allValid with
                      | true -> contentDnm "success"
                      | false -> contentDnm "error"
                use content = new StringContent(body, Encoding.UTF8, "application/json")
                let! res =  client.PostAsync(s, content) |> Async.AwaitTask
                log.Info (sprintf "got response code: %A" res.StatusCode)
              | _ -> ()
            | _ -> ()
        | _ -> ()



    }
    runner |> Async.RunSynchronously
    ContentResult(Content = "", ContentType = "text/html")