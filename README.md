<p align="center">
  <img src="assets/icon.png" width="92" alt="AlgHelper">
</p>

<h1 align="center">AlgHelper</h1>

<p align="center">
  Mała aplikacja tray do szybkiego wrzucania snippetów C# do Visual Studio.
</p>

<p align="center">
  <img src="https://img.shields.io/badge/wersja-2.0-2563eb?style=for-the-badge" alt="wersja">
  <img src="https://img.shields.io/badge/.NET-8.0-512bd4?style=for-the-badge&logo=dotnet&logoColor=white" alt=".NET 8">
  <img src="https://img.shields.io/badge/Visual%20Studio-snippety-5c2d91?style=for-the-badge" alt="Visual Studio snippety">
</p>

## O co chodzi

AlgHelper to mały program do traya. Po uruchomieniu może wrzucić gotowe snippety C# do Visual Studio, a po zamknięciu sprząta po sobie folder roboczy.

Program tworzy własny folder:

```text
Documents\Visual Studio 2022\Code Snippets\Visual C#\My Code Snippets\AlgHelper
```

Ten folder jest traktowany jako tymczasowy. Przy wyjściu program usuwa cały folder `AlgHelper`, ale dopiero po sprawdzeniu, czy to faktycznie jego katalog i czy istnieje marker `.alghelper-owned`.

Program nie czyści systemowych rzeczy typu Prefetch, logi Windows albo Defender. Rusza tylko swój folder ze snippetami.

## Jak używać

1. Uruchom `AlgHelper.exe`.
2. Kliknij ikonę programu w trayu.
3. Wybierz `Zainstaluj / odśwież`.
4. W Visual Studio wpisz skrót, np. `alg_38_binarne`.
5. Naciśnij `Tab` `Tab`.

Można też użyć skrótu klawiszowego:

```text
Right Shift + Backspace
```

Pierwsze użycie skrótu instaluje albo odświeża snippety. Kolejne użycie, gdy snippety są już aktywne, zamyka program i odpala sprzątanie.

## Menu w trayu

| Opcja | Działanie |
|---|---|
| `Zainstaluj / odśwież` | zapisuje aktualne snippety do folderu Visual Studio |
| `Folder snippetów` | otwiera główny folder snippetów Visual Studio |
| `Licznik aktywnych` | pokazuje ile snippetów jest aktualnie zainstalowanych |
| `Powiadomienia` | włącza albo wyłącza dymki z trayu |
| `O AlgHelper` | pokazuje wersję i podstawowe info |
| `Zamknij i posprzątaj` | zamyka program i usuwa folder `AlgHelper` |

## Skrót globalny

| Skrót | Działanie |
|---|---|
| `Right Shift + Backspace` | instaluje snippety, a przy kolejnym użyciu zamyka program |

Samo `Backspace` jest blokowane podczas używania tego skrótu, żeby przypadkiem nic nie skasować w edytorze.

Jeżeli Visual Studio jest uruchomione jako administrator, a AlgHelper normalnie, skrót może działać różnie. Wtedy najlepiej odpalić oba programy na tym samym poziomie uprawnień.

## Snippety

W tej wersji jest **44 snippetów** z labów 3-7. Skrót wpisuje się w edytorze C#, potem `Tab` `Tab`.

### Lab 3

| Skrót | Co wstawia |
|---|---|
| `alg_31_babelkowe_podstawowe` | Sortowanie bąbelkowe bez optymalizacji, z porównaniami i zamianami. |
| `alg_32_babelkowe_optymalne` | Bubble sort z flagą swapped, czyli koniec gdy tablica już jest ok. |
| `alg_33_wybor` | Sortowanie przez wybór, z liczeniem porównań i zamian. |
| `alg_34_wstawianie` | Sortowanie przez wstawianie, prosta wersja z liczeniem operacji. |
| `alg_35_quicksort` | QuickSort z pivotem na końcu tablicy. |
| `alg_36_liniowe` | Wyszukiwanie liniowe z liczeniem porównań. |
| `alg_37_liniowe_wartownik` | Wyszukiwanie liniowe z wartownikiem na końcu tablicy. |
| `alg_38_binarne` | Wyszukiwanie binarne i krótkie porównanie z liniowym. |

### Lab 4

| Skrót | Co wstawia |
|---|---|
| `alg_41_lucas` | Lucas: rekurencja, memoizacja, bottom-up i wersja ogonowa. |
| `alg_42_tetranacci` | Tetranacci w kilku wersjach: rec, memo, bottom-up i tail. |
| `alg_43_nwd` | Algorytm Euklidesa w wersji rekurencyjnej i iteracyjnej. |
| `alg_44_pascal` | Pascal rekurencyjnie i iteracyjnie. |
| `alg_45_mergesort` | Merge Sort dla tablicy int, z osobną funkcją Merge. |
| `alg_46_heapsort` | Heap Sort z funkcją Heapify. |
| `alg_47_hanoi` | Wieże Hanoi rekurencyjnie, plus liczba ruchów dla kilku n. |

### Lab 5

| Skrót | Co wstawia |
|---|---|
| `alg_510_nawiasy` | Sprawdzenie poprawności nawiasów za pomocą stosu. |
| `alg_51_lista_jedno` | Lista jednokierunkowa: dodawanie, usuwanie, szukanie i wypisanie. |
| `alg_52_lista_ogon` | Lista jednokierunkowa z head/tail, szybkie dodawanie na koniec. |
| `alg_53_wydarzenia` | Prosta lista wydarzeń, ta sama data dopisuje opis. |
| `alg_54_lista_dwu` | Lista dwukierunkowa z podstawowymi operacjami. |
| `alg_55_anagramy` | Grupowanie anagramów przez wspólny klucz w Dictionary. |
| `alg_56_duplikaty` | Usuwanie duplikatów po ID z użyciem HashSet. |
| `alg_57_stos` | Dwie wersje stosu: tablica i lista. |
| `alg_57_stos_lista` | Stos LIFO na liście jednokierunkowej. |
| `alg_57_stos_tab` | Stos LIFO na tablicy. |
| `alg_58_kolejka_tab` | Kolejka FIFO na tablicy cyklicznej. |
| `alg_59_palindrom` | Sprawdzenie palindromu przez stos i kolejkę. |

### Lab 6

| Skrót | Co wstawia |
|---|---|
| `alg_61_kolejka_prio` | Prosty przykład wbudowanej PriorityQueue. |
| `alg_62_kolejka_max` | Kolejka max zrobiona na SortedDictionary. |
| `alg_63_kolejka_czas` | Priorytet i czas dodania, gdy dwa zadania mają ten sam priorytet. |
| `alg_64_zgloszenia` | Obsługa zgłoszeń przez kolejkę priorytetową. |
| `alg_65_scal_tablice` | Scalanie kilku posortowanych tablic przez PriorityQueue. |
| `alg_66_zakres_tablice` | Najmniejszy zakres, który bierze po jednym elemencie z każdej tablicy. |

### Lab 7

| Skrót | Co wstawia |
|---|---|
| `alg_71_graf_wszerz` | BFS, czyli przejście grafu wszerz. |
| `alg_72_graf_wglab` | DFS w dwóch wersjach: rekurencja i stos. |
| `alg_72_graf_wglab_rek` | DFS rekurencyjnie. |
| `alg_72_graf_wglab_stos` | DFS iteracyjnie na stosie. |
| `alg_73_graf_skier` | Graf skierowany, przejście BFS i DFS. |
| `alg_74_graf_spojny` | Sprawdzenie spójności grafu przez BFS. |
| `alg_75_dijkstra` | Dijkstra, najkrótsze drogi od startu. |
| `alg_76_drogi_miasta` | Dijkstra dla miast, razem z odtworzeniem trasy. |
| `alg_76_kruskal` | Kruskal i zbiory rozłączne do MST. |
| `alg_77_drzewo_min` | MST w jednym przykładzie: Kruskal i Prim. |
| `alg_77_prim` | Prim, minimalne drzewo rozpinające. |


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
