# ue4ss tool — 인수인계 문서 (AGENTS.md)

> 작성일 2026-11-15. 폴더의 코드·프로젝트 파일·기존 문서와 실제 게임 설치 폴더(Steam `.acf`, `UnityPlayer.dll`, `MelonLoader.dll`)를 읽어 정리했다.
> 이 문서 작성 시점에 인게임 재검증은 하지 않았다. **"실측"** 표시는 디스크에서 직접 읽은 값, **"미확인"**은 다음 세션에서 확인할 항목이다.

## 0. 프로젝트 개요

- **무엇인가:** UE4SS 설치·관리 도구(Avalonia, net10-windows). 스팀 라이브러리의 Unreal 게임 탐지, UE4SS 릴리스 설치·상태 확인. 테스트 `Ue4ssTool.Tests`, 문서 `docs/`.
- **종류:** 게임 모드가 아닌 앱/도구
- **최근 수정일(파일 기준):** 2026-11-05
- **git 원격:** `H:\local-repos\ue4ss`
- **최근 커밋:**
  - `842494f Add GitHub Sponsors funding configuration`
  - `d094c2c feat: UE4SS 디버그 콘솔 원클릭 켜기/끄기 토글 기능 및 상태 감지 추가`
  - `65df8e6 feat: add generated 3D app icon and configure application icon`
  - `9ac5fb4 chore: remove license and license badge`
  - `0225d4e feat: initial commit of ue4ss-tool with full documentation`
- **먼저 읽을 기존 문서:**
  - [README.md](README.md)
  - [docs\README.md](docs/README.md)
  - [docs\architecture.md](docs/architecture.md)
  - [docs\development-guide.md](docs/development-guide.md)
  - [docs\engine-detection.md](docs/engine-detection.md)
  - [docs\overview.md](docs/overview.md)
  - [docs\ue4ss-lifecycle.md](docs/ue4ss-lifecycle.md)

## 1. 게임 버전 및 고정 여부

- 게임 모드가 아니므로 해당 없음.

## 2. 런타임 백엔드 및 디컴파일 경로

- 게임 모드가 아님. 런타임/언어: .NET (`net10.0-windows`)
- 모드/앱 프로젝트 `ue4ss tool\ue4ss tool.csproj` 타깃 프레임워크: `net10.0-windows` (SDK 스타일)
- 모드/앱 프로젝트 `Ue4ssTool.Tests\Ue4ssTool.Tests.csproj` 타깃 프레임워크: `net10.0-windows` (SDK 스타일)

## 3. 설계 의도와 우회 사유 (Rationale)

코드 주석과 기존 문서에서 작성자가 남긴 의도·주의 문장을 그대로 옮겼다(파일:줄).

- `README.md:26` — UE4SS는 프록시 DLL(`dwmapi.dll`) 방식으로 동작하므로 반드시 **실제 게임 exe가 있는 폴더**에 설치해야만 정상 로드됩니다. **`ue4ss tool`은 수많은 Steam 라이브러리에서 실제 게임 바이너리 위치를 정확히 찾아내고 안전하게 모드 로더를 구성해 줍니다.**
- `README.md:37` — 🔄 **스마트 업데이트 & 모드 자산 보존**: 핵심 모드 로더 파일만 최신화하고, 사용자가 작성한 모드 폴더 및 `mods.txt`, `mods.json`, 설정 파일은 안전하게 유지.
- `docs\architecture.md:3` — `ue4ss tool`은 Avalonia UI 기반의 프레젠테이션 계층과 도메인 로직(스팀 감지, 언리얼 분석, UE4SS 수명주기)이 명확히 분리된 구조로 설계되어 있습니다.
- `docs\architecture.md:112` — 핵심 모듈별 책임 및 인터페이스
- `docs\architecture.md:130` — `steamapps\appmanifest_<appid>.acf`의 `installdir`과 `name`을 맵핑하여 폴더명 대신 공식 스토어 게임 제목을 표시.
- `docs\development-guide.md:59` — 진행 상태 업데이트는 반드시 `Dispatcher.UIThread.Post(...)`를 통해 메인 스레드로 디스패치합니다.
- `docs\development-guide.md:61` — 핵심 로직(`UnrealInspector`, `Ue4ssInstaller` 등)의 많은 내부 헬퍼는 `internal`로 선언되어 있으며, `[assembly: InternalsVisibleTo("Ue4ssTool.Tests")]`를 통해 테스트 프로젝트에서 직접 접근하여 검증할 수 있습니다.
- `docs\engine-detection.md:50` — 엔진 버전은 UE4SS의 호환성을 결정짓는 가장 중요한 기준입니다. 2단계 판별 전략을 사용합니다.
- `ue4ss tool\MainWindow.axaml.cs:187` — 이미 설치된 UE4SS 와 같은 배치의 패키지를 고른다. 배치가 다르면 업데이트가 막히기 때문이다.
- `ue4ss tool\SteamCatalog.cs:21` — 목록에 폴더명 대신 스토어 이름을 보여 주기 위해서만 쓴다. 읽지 못해도 스캔은 동작한다.
- `ue4ss tool\Ue4ssReleases.cs:140` — 전에 받아 둔 패키지. GitHub 에 닿지 못할 때 목록 대신 쓴다.
- `ue4ss tool\UnrealInspector.cs:159` — UE3 도 Binaries 를 쓰지만 Content 대신 *Game\Cooked* 구조다. 여기서는 UE4/5 만 본다.

## 4. 핵심 훅 시그니처와 주입 파이프라인

- **주요 소스 파일(큰 순):**
  - `ue4ss tool\MainWindow.axaml.cs`
  - `ue4ss tool\Ue4ssInstaller.cs`
  - `ue4ss tool\UnrealInspector.cs`
  - `Ue4ssTool.Tests\InstallerTests.cs`
  - `ue4ss tool\Ue4ssState.cs`
  - `ue4ss tool\Ue4ssReleases.cs`
  - `Ue4ssTool.Tests\DetectionTests.cs`
  - `ue4ss tool\SteamLibraries.cs`
  - `ue4ss tool\UnrealGame.cs`
  - `ue4ss tool\Ue4ssSettings.cs`
  - `ue4ss tool\UnrealScanner.cs`
  - `Ue4ssTool.Tests\Ue4ssSettingsTests.cs`

## 5. 시도했다가 실패한 것들 (Dead Ends)

코드 주석과 기존 문서에서 실패·크래시·금지 관련 문장을 그대로 옮겼다(파일:줄). 같은 방법을 다시 시도하기 전에 해당 위치를 읽을 것.

- `README.md:35` — 🛠️ **디버그 & GUI 콘솔 관리**: 순정 패키지에서 기본값 `0`(꺼짐)으로 되어 있는 `UE4SS-settings.ini`의 `ConsoleEnabled`, `GuiConsoleEnabled`, `GuiConsoleVisible` 값을 설치/업데이트 시 자동으로 `1`로 고정하며, 관리 패널에서 원클릭으로 **「콘솔 끄기/켜기」**를 언제든 토글 가능.
- `docs\architecture.md:147` — `UE4SS-settings.ini`의 `[Debug]` 섹션을 분석하여 디버그 및 GUI 콘솔 창 설정을 강제로 `1`로 활성화.
- `docs\engine-detection.md:29` — `Binaries\Win64` 폴더 안에는 크래시 리포터나 설치 헬퍼, 웹 프로세스 등 다양한 exe가 공존합니다. 도구는 정교한 우선순위 규칙에 따라 실제 게임 exe를 선정합니다.
- `docs\engine-detection.md:77` — 청크 경계에 문자열이 잘려서 검색되지 않는 문제를 방지하기 위해 **64바이트 오버랩(Overlap)**을 적용하여 이전 청크의 끝부분을 다음 청크 앞단에 복사합니다.
- `docs\overview.md:9` — 언리얼 엔진 기반 게임에 UE4SS 모드 로더를 수동으로 설치할 때 모더들이 직면하는 대표적인 문제점들을 해결합니다.
- `docs\overview.md:25` — 이 둘이 한 폴더에 섞이면 심각한 크래시가 발생하거나 어떤 모드가 로드되는지 추적하기 어렵습니다.
- `docs\overview.md:28` — GitHub에서 제공되는 기본 RE-UE4SS 릴리스는 `UE4SS-settings.ini`의 디버그/GUI 콘솔 설정이 기본적으로 `0`으로 꺼져 있습니다:
- `docs\ue4ss-lifecycle.md:58` — 설치를 진행하기 전, 기존 게임 파일이나 설정이 망가지지 않도록 [Ue4ssInstaller.Preflight](file:///h:/source/repos/ue4ss%20tool/ue4ss%20tool/Ue4ssInstaller.cs)에서 충돌을 검사합니다. 충돌이 감지되면 `InstallBlockedException`을 던져 작업을 중단합니다:
- `ue4ss tool\MainWindow.axaml.cs:125` — async void 이므로 예외가 새어 나가면 프로세스가 죽는다. 반드시 여기서 잡는다.
- `ue4ss tool\Program.cs:8` — Initialization code. Don't use any Avalonia, third-party APIs or any
- `ue4ss tool\Program.cs:15` — Avalonia configuration, don't remove; also used by visual designer.
- `ue4ss tool\Ue4ssInstaller.cs:97` — 파일을 쓰지 않고 zip 구조와 기존 설치와의 충돌만 확인한다. 막히면 <see cref="InstallBlockedException"/>.
- `ue4ss tool\Ue4ssSettings.cs:12` — 기본 패키지(zDEV 가 아닌 것)는 <c>[Debug]</c> 의 세 값이 모두 0 이라, 게임을 켜도 UE4SS 창이 하나도 뜨지 않는다.

## 6. 빌드 명령 및 배포 경로

- 솔루션: `ue4ss tool.slnx`
- `ue4ss tool\ue4ss tool.csproj`:
  ```powershell
  dotnet build "ue4ss tool\ue4ss tool.csproj" -c Release
  ```
- `Ue4ssTool.Tests\Ue4ssTool.Tests.csproj`:
  ```powershell
  dotnet build "Ue4ssTool.Tests\Ue4ssTool.Tests.csproj" -c Release
  ```

## 6.5 2026-11-23 감사 요약

- 중복 제거(`Ue4ssSettings.Rewrite`, `MainWindow.IsGameRunningAsync/Launch`)와 릴리스 목록 동시 요청(`Task.WhenAll`)만 적용. 상세는 `docs/architecture.md` 마지막 절.
- 테스트 94개 통과(실측). GUI 실기 확인은 **미확인**.

## 7. 다음 작업자에게

- 작업 전 기존 문서(0절)부터 읽는다.
- 인게임 동작을 확인하면 3·5절의 **미확인** 항목을 실제 내용으로 바꾼다.
