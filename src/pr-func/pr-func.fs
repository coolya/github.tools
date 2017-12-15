namespace pr.func


open Microsoft.AspNetCore.Mvc
open Microsoft.AspNetCore.Http
open Microsoft.Azure.WebJobs.Host
open Newtonsoft.Json.Linq


module PrecompiledHttp =
  open System
  open System.Net.Http
  open System.Net.Http.Headers
  open System.Text


  let content = sprintf """{
  "state": "%s",
  "description" : "pull request WIP status",
  "context": "pr/wip"
}"""

  let run(req: HttpRequestMessage, log: TraceWriter) =
    let runner = async {
        let! txt = req.Content.ReadAsStringAsync() |> Async.AwaitTask
        let jobj = JObject.Parse txt

        let get key (token : JToken) = 
            token.[key] |> Option.ofObj
        let value (token: JToken) = 
            token.Value<_>()
        let pr = jobj |> get "pull_request"
        let title: string option =  pr |> Option.bind (get "title") |> Option.map value
        let stats: string option = pr |> Option.bind (get "statuses_url")  |> Option.map value

        match (title, stats) with 
        | (Some t, Some s) -> 
          let isWip = t.StartsWith("WIP:") || t.StartsWith("[WIP]")
          let body = match isWip with
                      | true -> content "error"
                      | false -> content "success"

          let auth = Environment.GetEnvironmentVariable("GITHUB_CREDENTIALS")
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
          let! resbody = res.Content.ReadAsStringAsync() |> Async.AwaitTask
          log.Info (sprintf "got response code: %A with body: %s" res.StatusCode resbody) 
        | _ -> ()

    }
    runner |> Async.RunSynchronously
    ContentResult(Content = "", ContentType = "text/html")