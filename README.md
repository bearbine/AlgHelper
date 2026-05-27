<p align="center">
  <img src="assets/icon.png" width="92" alt="AlgHelper">
</p>

<h1 align="center">AlgHelper</h1>

<p align="center">
  Mała aplikacja tray do instalowania snippetów C# dla Visual Studio.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/wersja-1.8.1-2563eb?style=for-the-badge" alt="wersja">
  <img src="https://img.shields.io/badge/.NET-8.0-512bd4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 8">
  <img src="https://img.shields.io/badge/Visual%20Studio-snippety-5c2d91?style=for-the-badge" alt="Visual Studio snippety">
</p>



## Co robi program

AlgHelper siedzi w trayu i jednym kliknięciem wrzuca gotowe snippety do Visual Studio. Snippety są trzymane w osobnym folderze `AlgHelper`, więc aplikacja wie, co sama dodała i sprząta po sobie przy zamknięciu. Jest też watchdog, który usuwa folder snippetów, gdy aplikacja zostanie ubita albo wywali się w trakcie działania.

Folder snippetów:

```text
Documents\Visual Studio 2022\Code Snippets\Visual C#\My Code Snippets\AlgHelper
```

## Jak używać

1. Uruchom `AlgHelper.exe`.
2. Kliknij ikonę w trayu.
3. Wybierz `Zainstaluj / odśwież`.
4. W Visual Studio wpisz skrót, na przykład `alg_77_drzewo_min`.
5. Naciśnij `Tab` `Tab`.

## Budowanie

Najprościej:

```powershell
.\build_single_exe.ps1
```

Albo przez plik:

```bat
build_single_exe.bat
```

Gotowy plik pojawi się tutaj:

```text
release\AlgHelper-1.8.1.exe
```

To jest normalny plik `.exe`, bez ZIP-a i bez wyodrębniania. Przy zupełnie świeżym klonie można najpierw zrobić jednorazowo:

```powershell
dotnet restore -r win-x64
```

## Menu w trayu

| Opcja | Działanie |
|---|---|
| `Zainstaluj / odśwież` | zapisuje aktualne snippety do folderu Visual Studio |
| `Folder snippetów` | otwiera główny folder snippetów Visual Studio |
| `Licznik aktywnych` | pokazuje ile snippetów jest aktualnie zainstalowanych |
| `Powiadomienia` | włącza lub wyłącza dymki z trayu |
| `O AlgHelper` | pokazuje wersję i autora |
| `Zamknij i posprzątaj` | zamyka program i usuwa folder snippetów AlgHelper |

## Skróty snippetów

Wszystkie snippety wpisujesz w edytorze C#, potem `Tab` `Tab`.

### Lab 5

| Skrót | Co wstawia |
|---|---|
| `alg_51_lista_jedno` | lista jednokierunkowa |
| `alg_52_lista_ogon` | lista jednokierunkowa z ogonem |
| `alg_53_wydarzenia` | lista wydarzeń sortowana po dacie |
| `alg_54_lista_dwu` | lista dwukierunkowa |
| `alg_55_anagramy` | grupowanie anagramów przez słownik |
| `alg_56_duplikaty` | usuwanie duplikatów przez `HashSet` |
| `alg_57_stos` | stos tablicowy i listowy w jednym przykładzie |
| `alg_57_stos_tab` | stos na tablicy |
| `alg_57_stos_lista` | stos na liście |
| `alg_58_kolejka_tab` | kolejka FIFO na tablicy cyklicznej |
| `alg_59_palindrom` | sprawdzanie palindromu |
| `alg_510_nawiasy` | sprawdzanie poprawności nawiasów |

### Lab 6

| Skrót | Co wstawia |
|---|---|
| `alg_61_kolejka_prio` | wbudowana `PriorityQueue` |
| `alg_62_kolejka_max` | kolejka priorytetowa max |
| `alg_63_kolejka_czas` | kolejka priorytetowa z czasem dodania |
| `alg_64_zgloszenia` | obsługa zgłoszeń przez priorytety |
| `alg_65_scal_tablice` | scalanie kilku posortowanych tablic |
| `alg_66_zakres_tablice` | najmniejszy zakres z kilku tablic |

### Lab 7

| Skrót | Co wstawia |
|---|---|
| `alg_71_graf_wszerz` | przeszukiwanie grafu wszerz, czyli BFS |
| `alg_72_graf_wglab` | DFS rekurencyjny i iteracyjny w jednym przykładzie |
| `alg_72_graf_wglab_rek` | DFS rekurencyjny |
| `alg_72_graf_wglab_stos` | DFS iteracyjny ze stosem |
| `alg_73_graf_skier` | graf skierowany, BFS i DFS |
| `alg_74_graf_spojny` | sprawdzanie spójności grafu |
| `alg_75_dijkstra` | najkrótsze ścieżki Dijkstry |
| `alg_76_drogi_miasta` | Dijkstra dla tras między miastami |
| `alg_76_kruskal` | minimalne drzewo rozpinające Kruskala |
| `alg_77_drzewo_min` | pełny przykład MST: Kruskal i Prim |
| `alg_77_prim` | minimalne drzewo rozpinające Prima |

## Autor

bearbine  
<https://github.com/bearbine>

## Licencja

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
