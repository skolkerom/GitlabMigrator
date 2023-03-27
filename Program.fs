open System
open System.Net.Http
open System.IO
open System.Net.Http.Headers
open Newtonsoft.Json
open System.Diagnostics

let mutable downloadDirectory = ""
let mutable sourceGitlabUrl = ""
let mutable targetGitlabUrl = ""
let mutable sourceAccessToken = ""
let mutable targetAccessToken = ""
let mutable targetAccessTokenName = ""
let mutable groupPath = ""

type Result = | Success | Error of string

let (|Success|Error|) args =
    let rec loop = function
        | "--download-directory" :: value :: tail -> downloadDirectory <- value; loop tail
        | "--source-gitlab-url" :: value :: tail -> sourceGitlabUrl <- value; loop tail
        | "--source-access-token" :: value :: tail -> sourceAccessToken <- value; loop tail
        | "--target-gitlab-url" :: value :: tail -> targetGitlabUrl <- value; loop tail
        | "--target-access-token" :: value :: tail -> targetAccessToken <- value; loop tail
        | "--target-access-token-name" :: value :: tail -> targetAccessTokenName <- value; loop tail
        | "--group-path" :: value :: tail -> groupPath <- value; loop tail
        | _ -> if  sourceAccessToken <> "" && targetAccessToken <> "" &&
                   targetAccessTokenName <> "" && groupPath <> "" &&
                   sourceGitlabUrl <> "" && targetGitlabUrl <> ""
               then Success 
               else Error ("Usage: GitlabMigrator.exe --download-directory <download-directory> --source-gitlab-url <source-gitlab-url> --source-access-token <source-access-token> --target-gitlab-url <target-gitlab-url> --target-access-token <target-access-token> --target-access-token-name <target-access-token-name> --group-path <group-path>")

    loop args


type Project =
    { id: int
      name: string
      path_with_namespace: string
      kind: string
      web_url: string }

type Group =
    { id: int
      name: string
      web_url: string
      path: string
      full_path: string
      parent_id: Nullable<int>
      projects: Project list }
    
let executeCmd command =
    let prc = new Process();
    let startInfo = ProcessStartInfo (
        FileName = "cmd.exe",
        Arguments = "/c " + command,
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true
    )
    prc.StartInfo <- startInfo;
    prc.Start() |> ignore
    prc

let executeCmdAndWait command =
    let prc = executeCmd command
    prc.WaitForExit()


let getGroups baseUrl token (groupPath: string) withSubgroups = async {
    let httpClient = new HttpClient()
    httpClient.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Bearer", token)
    let mutable url = ""
    let fixedGroupPath = groupPath.Replace("/", "%2f")
    if(withSubgroups)
    then url <- $"{baseUrl}/api/v4/groups?with_subgroups=true&per_page=1000"
    else url <- $"{baseUrl}/api/v4/groups/{fixedGroupPath}?per_page=1000"
    
    let! result = httpClient.GetAsync(url) |> Async.AwaitTask
    let! content = result.Content.ReadAsStringAsync() |> Async.AwaitTask
    
    try
    return match withSubgroups with
                    | true ->  JsonConvert.DeserializeObject<seq<Group>>(content) |> Seq.filter (fun g -> g.full_path.StartsWith(groupPath))
                    | false -> [JsonConvert.DeserializeObject<Group>(content)]
    with
        | _  -> printfn $"{content}"
                return []
}

let getProjects(g: Group) = async {
    let fixedGroupPath = g.full_path.Replace("/", """%2f""")
    let httpClient = new HttpClient()
    httpClient.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Bearer", sourceAccessToken)
    let url = $"{sourceGitlabUrl}/api/v4/groups/{fixedGroupPath}?with_projects=true&per_page=1000"  
    let! result = httpClient.GetAsync(url) |> Async.AwaitTask
    let! content = result.Content.ReadAsStringAsync() |> Async.AwaitTask
    try
    let projects = JsonConvert.DeserializeObject<Group>(content).projects
    return projects
    with
    | _  -> printfn $"{content}"
            return []
}

let createProject (project: string) = async {
     let group = project.Substring(0, project.LastIndexOf("/"))
     let gidGroup = Async.RunSynchronously  (getGroups targetGitlabUrl targetAccessToken group false)
     let gid = (gidGroup |> Seq.head).id.ToString()
     
     let projectName = project.Substring(project.LastIndexOf("/") + 1)
     
     let url = $"{targetGitlabUrl}/api/v4/projects/"

     let data = 
         dict [ "name", projectName
                "path", projectName
                "namespace_id", gid
                "initialize_with_readme", "false" ]
         
     let content = new FormUrlEncodedContent(data)
     let request = new HttpRequestMessage(HttpMethod.Post, url)
     request.Content <- content
     
     let httpClient = new HttpClient()
     httpClient.DefaultRequestHeaders.Authorization <- AuthenticationHeaderValue("Bearer", targetAccessToken)
     let! response = httpClient.SendAsync(request) |> Async.AwaitTask
     let! _ = response.Content.ReadAsStringAsync() |> Async.AwaitTask
     ()
}

let transferProject (project: string) = async {
    let projectDirectory = Path.Combine(downloadDirectory, project)
    
    if(Directory.Exists(projectDirectory) |> not)
    then executeCmdAndWait($"git clone {sourceGitlabUrl}/{project}.git {projectDirectory}")
         createProject(project) |> Async.RunSynchronously
         
         executeCmdAndWait($"cd {projectDirectory} && git remote rename origin originOld")    
         executeCmdAndWait($"cd {projectDirectory} && git remote add origin https://{targetAccessTokenName}:{targetAccessToken}@gitlab.eurapartners.com/{project}.git")
   
         let result = executeCmd($"cd {projectDirectory} && git branch --list --remotes")
         
         while not (result.StandardOutput.EndOfStream) do
             let branch = result.StandardOutput.ReadLine()
             let b = branch.Substring(branch.LastIndexOf("/") + 1)
             executeCmdAndWait($"cd {projectDirectory} && git checkout {b}")
             executeCmdAndWait($"""cd {projectDirectory} && git push -u origin {b}""")
         printfn $"Project {project} successfully transferred"
    else printfn $"{projectDirectory} should be empty"
    ()
}

let run() =
    let groups = Async.RunSynchronously(getGroups sourceGitlabUrl sourceAccessToken groupPath true)
    let groupsWithProjects =
            groups
            |> Seq.collect (fun g -> Async.RunSynchronously(getProjects g))
    groupsWithProjects
    |> Seq.iter (fun p -> Async.RunSynchronously(transferProject (p.path_with_namespace)))

[<EntryPoint>]
let main args =
    let argArray = args |> Array.toList
    match argArray with
    | Error message -> printfn $"%s{message}"
    | Success  -> run()
    0
    
    


