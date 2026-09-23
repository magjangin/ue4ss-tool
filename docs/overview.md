# 개요 (Overview)

`ue4ss tool`은 설치된 Steam 게임 중 **언리얼 엔진(Unreal Engine 4/5/3) 게임을 자동으로 찾아내고, [RE-UE4SS](https://github.com/UE4SS-RE/RE-UE4SS) 모드 로더를 설치·업데이트·제거·비활성화**할 수 있도록 지원하는 Windows 데스크톱 GUI 도구입니다.

---

## 1. 해결하고자 하는 문제

언리얼 엔진 기반 게임에 UE4SS 모드 로더를 수동으로 설치할 때 모더들이 직면하는 대표적인 문제점들을 해결합니다.

### ① 실제 게임 실행 파일의 깊은 위치
대다수의 언리얼 엔진 게임은 설치 루트 폴더에 존재하는 `<게임>.exe`가 실제 본체가 아니라 스플래시 화면을 띄우거나 런처 역할을 하는 가벼운 래퍼(Wrapper)입니다.
실제 게임 로직과 엔진 코드가 들어있는 바이너리는 다음과 같은 깊은 하위 경로에 있습니다:
```
<설치폴더>\<ProjectName>\Binaries\Win64\<ProjectName>-Win64-Shipping.exe
```
UE4SS는 DirectX/Windows API 프록시 DLL(`dwmapi.dll`) 방식으로 작동하므로, **실제 게임 바이너리가 위치한 폴더**에 설치해야 프로세스가 시작될 때 로드됩니다. 최상위 폴더에 설치하면 아무런 동작도 하지 않습니다.

### ② 버전별 설치 레이아웃 혼선
UE4SS는 버전에 따라 폴더 배치 방식이 상이합니다:
* **최신 실험판 (v3.0.1+ Experimental)**: `dwmapi.dll`만 exe 옆에 두고, 나머지 본체(`UE4SS.dll`, `UE4SS-settings.ini`, `Mods/`)는 `ue4ss\` 하위 폴더에 격리.
* **구 안정판 (v3.0.x Stable)**: 모든 파일(`UE4SS.dll`, `Mods/` 등)이 exe 바로 옆에 공존(Flat).
* **레거시 (v2.x)**: `xinput1_3.dll` 프록시 사용.

이 둘이 한 폴더에 섞이면 심각한 크래시가 발생하거나 어떤 모드가 로드되는지 추적하기 어렵습니다.

### ③ 순정 배포본의 콘솔 창 비활성화
GitHub에서 제공되는 기본 RE-UE4SS 릴리스는 `UE4SS-settings.ini`의 디버그/GUI 콘솔 설정이 기본적으로 `0`으로 꺼져 있습니다:
```ini
[Debug]
ConsoleEnabled = 0
GuiConsoleEnabled = 0
GuiConsoleVisible = 0
```
이로 인해 모더가 UE4SS를 설치하고 게임을 실행해도 모드 로더가 정상 작동하는지 직관적으로 확인할 수 없습니다.

### ④ 모드 업데이트 및 삭제 시의 위험
* 업데이트 시 사용자가 직접 작성하거나 추가한 `Mods/` 폴더 내 모드 파일이나 `mods.txt` 설정이 덮어씌워져 유실될 위험이 있습니다.
* 제거 시 게임 본체 파일과 섞여 있는 경우 실수로 필수 바이너리를 지워 게임을 재검사(무결성 검사)해야 하는 불편함이 발생합니다.

---

## 2. 주요 기능 및 장점

| 기능 | 설명 |
|---|---|
| **고속 스캔** | 레지스트리와 `libraryfolders.vdf`, 드라이브 루트 보존본을 탐색하여 900+개 라이브러리도 1~2초 내 언리얼 게임만 추출 |
| **정밀 엔진 판별** | exe 버전 리소스(`FILEVERSION`) 및 바이너리 내 `++UE5+Release-` 문자열 스캔으로 정확한 엔진 버전 도출 |
| **스마트 패키징** | 최신 실험판과 안정판 릴리스를 GitHub에서 실시간 조회·다운로드하고 `%LOCALAPPDATA%`에 캐시 |
| **안전 설치 (Preflight)** | 기존 설치와의 충돌 검사, 실행 중인 프로세스 감지, 잘못된 경로 주입 차단 |
| **콘솔 설정 자동 패치** | 설치/업데이트 시 `ConsoleEnabled`, `GuiConsoleEnabled`, `GuiConsoleVisible`을 `1`로 자동 주입 |
| **사용자 자산 보존** | 업데이트 시 `mods.txt`, `mods.json`, 기존 커스텀 모드 디렉터리 완벽 유지 |
| **원클릭 비활성화** | DLL 삭제 없이 `dwmapi.dll` ↔ `dwmapi.dll.disabled` 이름 변경으로 간편한 토글 |
| **안전 제거** | UE4SS 관련 파일만 선별하여 윈도우 휴지통(Recycle Bin)으로 이동 (실수 복구 가능) |

---

## 3. 기술 스택

* **타깃 프레임워크**: `.NET 10` (`net10.0-windows`)
* **언어**: C# 13
* **GUI 프레임워크**: [Avalonia UI](https://avaloniaui.net/) 12.1.2 (Fluent Theme, Compiled Bindings 지원)
* **네트워크 / API**: `System.Net.Http`, GitHub Releases REST API
* **파일 압축 / 조작**: `System.IO.Compression`, `Microsoft.VisualBasic.FileIO.FileSystem` (휴지통 안전 삭제용)
* **단위 테스트**: xUnit 2.9 (92개 테스트 케이스 구축)
