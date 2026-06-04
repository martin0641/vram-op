Option Explicit

Dim fso
Dim shell
Dim scriptDir
Dim repoPath
Dim powershellPath
Dim syncScript
Dim command

Set fso = CreateObject("Scripting.FileSystemObject")
Set shell = CreateObject("WScript.Shell")

scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
repoPath = fso.GetParentFolderName(scriptDir)

If WScript.Arguments.Count > 0 Then
    repoPath = WScript.Arguments(0)
End If

powershellPath = shell.ExpandEnvironmentStrings("%SystemRoot%") & "\System32\WindowsPowerShell\v1.0\powershell.exe"
syncScript = fso.BuildPath(scriptDir, "Sync-VramOp.ps1")
command = """" & powershellPath & """ -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File """ & syncScript & """ -RepoPath """ & repoPath & """"

shell.Run command, 0, True
