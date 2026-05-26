<p align="center">
  <img src="assets/icon.png" width="92" alt="AlgHelper icon">
</p>

<h1 align="center">AlgHelper</h1>

<p align="center">
  <b>tiny tray app for Visual Studio C# snippets</b>
</p>

<p align="center">
  <img src="https://img.shields.io/badge/release-1.79release-7c3aed?style=for-the-badge" alt="release">
  <img src="https://img.shields.io/badge/.NET-8.0-512bd4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET">
  <img src="https://img.shields.io/badge/windows-tray_app-0d1117?style=for-the-badge&logo=windows&logoColor=white" alt="windows">
  <img src="https://img.shields.io/badge/C%23-snippets-239120?style=for-the-badge&logo=csharp&logoColor=white" alt="csharp">
</p>



---

## preview

<p align="center">
  <img src="assets/tray-preview.png" width="560" alt="AlgHelper tray menu">
</p>

## quick start

```powershell
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true --self-contained false
```

or:

```bat
build_single_exe.bat
```

output:

```text
bin\Release\net8.0-windows\win-x64\publish\AlgHelper.exe
```

## paths

| name | path |
|---|---|
| snippets folder | `Documents\Visual Studio 2022\Code Snippets\Visual C#\My Code Snippets\AlgHelper` |
| exe output | `bin\Release\net8.0-windows\win-x64\publish\AlgHelper.exe` |

## snippet usage

```text
alg_graf_bfs_71 + Tab + Tab
alg_lista_jedno_51 + Tab + Tab
alg_pq_builtin_61 + Tab + Tab
```

## project structure

```text
AlgHelper/
├─ Program.cs
├─ AlgHelper.csproj
├─ alghelper.ico
├─ Resources/
├─ snippets/
├─ assets/
├─ README.md
├─ LICENSE
├─ .gitignore
├─ build_single_exe.bat
└─ build_single_exe.ps1
```

## tray actions

| action | what it does |
|---|---|
| install / refresh | creates `AlgHelper` snippet folder and writes snippets |
| remove snippets | deletes the whole `AlgHelper` snippet folder |
| open folder | opens the main Visual Studio snippets directory |
| count snippets | shows how many snippets are installed |
| notifications | toggles tray balloons |
| about | shows author info and GitHub link |
| exit | closes app and cleans its snippet folder |

## author

**bearbine**  
<https://github.com/bearbine>

## license

MIT License

Copyright (c) 2026 bearbine

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
