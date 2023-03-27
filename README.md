### GitlabMigrator

#### Description
Ad hoc utility for migrating groups with projects from one gitlab instance to another.</br>
It was tested on source gitlab version 14.4.1-ee and target gitlab version 15.8.0-ee

#### Usage
```GitlabMigrator.exe --download-directory <download-directory> --source-gitlab-url <source-gitlab-url> --source-access-token <source-access-token> --target-gitlab-url <target-gitlab-url> --target-access-token <target-access-token> --target-access-token-name <target-access-token-name> --group-path <group-path>```

#### How it works
1. Pulls repositories from specified group-path on source gitlab instance and save it to download-directory
2. Creates projects with the same names on target gitlab instance
3. Renames current source git origin to originOld and creates new origin targeted to target
4. Pushes all branches and tags into target gitlab instance

#### ***Important!*** <br/>
Before start ensure you have enough space to pull all projects from group-path<br/>
Git origin will be re-targeted to target gitlab repositories for all projects in group-path<br/>
Group path parameter (group-path) is name of the group that need to be exported with no trailing '/', for example, it-dev/fx