# 🎮 ue4ss tool

<div align="center">

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white)
![Avalonia UI](https://img.shields.io/badge/Avalonia%20UI-12.1.2-8B5CF6?logo=avalonia&logoColor=white)
![Platform](https://img.shields.io/badge/Platform-Windows-0078D6?logo=windows&logoColor=white)
![Tests](https://img.shields.io/badge/Tests-92%20passed-brightgreen?logo=xunit&logoColor=white)

**Steam에 설치된 언리얼 엔진(UE4/UE5/UE3) 게임을 자동으로 찾아내고,<br/>[RE-UE4SS](https://github.com/UE4SS-RE/RE-UE4SS) 모드 로더를 원클릭으로 설치·업데이트·관리하는 Windows 데스크톱 도구**

[sister project: [steam-game-tool](https://github.com/magjangin/steam-game-tool) (Unity Mono/IL2CPP 판별기 및 멜론로더 설치기)]

</div>

---

> [!IMPORTANT]
> **왜 이 도구가 필요한가요?**
>
> 언리얼 엔진 게임은 설치 폴더 바로 아래의 `<게임>.exe`가 실제 본체가 아니라 런처에 불과합니다. 실제 게임 엔진과 바이너리는 `<설치폴더>\<프로젝트>\Binaries\Win64\<프로젝트>-Win64-Shipping.exe`와 같은 깊은 하위 경로에 위치합니다.
>
> UE4SS는 프록시 DLL(`dwmapi.dll`) 방식으로 동작하므로 반드시 **실제 게임 exe가 있는 폴더**에 설치해야만 정상 로드됩니다. **`ue4ss tool`은 수많은 Steam 라이브러리에서 실제 게임 바이너리 위치를 정확히 찾아내고 안전하게 모드 로더를 구성해 줍니다.**

---

## ✨ 핵심 기능

* ⚡ **초고속 라이브러리 스캔**: Windows 레지스트리와 Steam `libraryfolders.vdf`, 드라이브 루트 보존본을 탐색하여 수백 개의 게임 중 언리얼 엔진 게임만 1~2초 내에 고속 추출.
* 🎯 **정밀 엔진 버전 판별**: 실행 파일의 `FILEVERSION` 리소스 및 바이너리 내 `++UE5+Release-` / `++UE4+Release-` 브랜치 문자열(4MB 스트림 청크 + 64B 오버랩)을 분석하여 정확한 엔진 버전(UE 4.0~5.8) 판별.
* 📦 **GitHub 릴리스 자동 연동**: [RE-UE4SS](https://github.com/UE4SS-RE/RE-UE4SS)의 최신 안정판(v3.0.1) 및 실험판(experimental-latest)을 GitHub API로 조회하여 원클릭 다운로드 및 로컬 캐싱.
* 🛠️ **디버그 & GUI 콘솔 자동 활성화**: 순정 패키지에서 기본값 `0`(꺼짐)으로 되어 있는 `UE4SS-settings.ini`의 `ConsoleEnabled`, `GuiConsoleEnabled`, `GuiConsoleVisible` 값을 설치/업데이트 시 자동으로 `1`로 고정.
* 🛡️ **안전한 설치 (Preflight Check)**: 기존 설치 레이아웃과의 충돌 방지, 타 모드 DLL 보호, 게임 실행 중 설치 차단.
* 🔄 **스마트 업데이트 & 모드 자산 보존**: 핵심 모드 로더 파일만 최신화하고, 사용자가 작성한 모드 폴더 및 `mods.txt`, `mods.json`, 설정 파일은 안전하게 유지.
* 📴 **원클릭 비활성화 (끄기/켜기)**: 파일 삭제 없이 프록시 DLL 이름을 `dwmapi.dll` ↔ `dwmapi.dll.disabled`로 토글하여 순정 상태로 즉시 복구.
* 🗑️ **휴지통(Recycle Bin) 안전 제거**: 게임 본체 파일은 일절 건드리지 않고 UE4SS 관련 파일만 선별하여 윈도우 휴지통으로 이동 (언제든 복원 가능).
* 🚨 **안티치트 경고 배지**: EasyAntiCheat(EAC) 및 BattlEye 적용 여부를 감지하여 온라인 계정 제재 위험 사전 경고.

---

## 🚀 빠른 시작

### 요구 사항
* **운영체제**: Windows 10 버전 1809 이상 또는 Windows 11 (x64)
* **런타임/SDK**: [.NET 10 SDK](https://dotnet.microsoft.com/download)

### 실행 방법

```powershell
# 저장소 클론
git clone https://github.com/magjangin/ue4ss-tool.git
cd ue4ss-tool

# 애플리케이션 실행
dotnet run --project "ue4ss tool/ue4ss tool.csproj"
```

### 기본 사용 절차
1. **「스캔 시작」** 클릭: 모든 Steam 라이브러리와 외부 보존본을 스캔합니다.
2. **「보기 필터」** 선택: UE5 / UE4 / UE3 / UE4SS 설치됨 / 미설치 / 안티치트 별로 게임을 필터링합니다.
3. **게임 선택**: 오른쪽 패널에서 엔진 버전, 실제 게임 exe 위치, IoStore 여부, UE4SS 호환성을 확인합니다.
4. **UE4SS 패키지 선택 후 「설치」**: GitHub에서 패키지를 받아 설치될 파일 확인 창을 거친 뒤 안전하게 설치합니다.
5. **관리**: 이미 설치된 게임은 **「업데이트」**, **「끄기/켜기」**, **「제거 (휴지통)」**, **「설정 파일 / Mods 폴더 / 로그 열기」** 버튼으로 손쉽게 관리합니다.

---

## 📊 UE4SS 버전 지원 범위

RE-UE4SS 공식 릴리스 기준:

| 엔진 버전 | 안정판 (v3.0.1) | 최신 실험판 (experimental-latest) | 비고 |
|---|:---:|:---:|---|
| **UE3, UE 4.6 이하** | ✗ | ✗ | UE4SS 미지원 (정보 표시만 제공) |
| **UE 4.7 ~ 4.10** | ✗ | **✓** | 실험판 필수 |
| **UE 4.11 ~ 5.3** | **✓** | **✓** | 안정판 및 실험판 모두 지원 |
| **UE 5.4 ~ 5.8** | ✗ | **✓** | 실험판 필수 (최신 UE5 지원) |
| **개발/테스트 빌드** | ✗ | **✓** | Non-Shipping 바이너리 |

> [!TIP]
> 기본 선택은 지원 범위가 가장 넓은 **최신 실험판**입니다. 이미 설치된 게임을 선택하면 설치된 구조와 일치하는 패키지를 자동으로 선택합니다.

---

## 📁 패키지 레이아웃

UE4SS 버전에 따라 배치 구조가 상이하며, 도구가 이를 자동으로 구분하여 관리합니다.

| 레이아웃 | 폴더 구조 | 해당 버전 |
|---|---|---|
| **Subfolder (하위 폴더)** | `dwmapi.dll` + `ue4ss\UE4SS.dll`, `ue4ss\UE4SS-settings.ini`, `ue4ss\Mods\` | 최신 실험판 |
| **Flat (exe 옆)** | `dwmapi.dll`, `UE4SS.dll`, `UE4SS-settings.ini`, `Mods\` 모두 exe 옆 | 구 안정판 v3.0.x |
| **Legacy** | `xinput1_3.dll` + `UE4SS.dll` | 레거시 v2.x (감지만 지원) |

---

## 📚 상세 문서 (Documentation)

자세한 내부 동작 원리와 개발 관련 문서는 **[`docs/`](docs/README.md)**에서 확인하실 수 있습니다:

* **[개요 및 해결 과제](docs/overview.md)**: 런처 바이너리 vs 실제 Shipping 바이너리, 프록시 주입 원리
* **[시스템 아키텍처](docs/architecture.md)**: 계층 구조 및 스캔/설치 데이터 흐름도 (Mermaid 다이어그램)
* **[언리얼 엔진 판별 알고리즘](docs/engine-detection.md)**: BFS 프로젝트 탐색, exe 우선순위, 4MB 스트림 바이트 스캔
* **[UE4SS 생명주기 관리](docs/ue4ss-lifecycle.md)**: 사전 충돌 검증(Preflight), 설정 강제 활성화, 휴지통 삭제
* **[개발 및 테스트 가이드](docs/development-guide.md)**: 빌드 방법, 92개 xUnit 단위 테스트 구조 및 기여 안내

---

## 🧪 테스트 실행

모의 Steam 라이브러리(`FakeLibrary`) 환경을 기반으로 작성된 92개의 xUnit 테스트를 제공합니다.

```powershell
dotnet test "ue4ss tool.slnx"
```

```text
통과!  - 실패: 0, 통과: 92, 건너뜀: 0, 전체: 92, 기간: 458 ms - Ue4ssTool.Tests.dll (net10.0)
```

---

## 🔒 안전성 보장

* **읽기 전용 스캔**: 스캔 과정에서 게임 파일을 일절 수정하거나 실행하지 않습니다.
* **사용자 사전 승인**: 파일 쓰기/삭제 전 영향받는 파일 목록을 확인 창에서 보여주고 승인된 경우에만 진행합니다.
* **휴지통 보존**: 모드 로더 제거 시 영구 삭제하지 않고 Windows 휴지통으로 안전하게 이동합니다.
* **관리자 권한 불필요**: 일반 사용자 권한으로 모든 Steam 라이브러리 조작이 가능합니다.

