# 개발 및 테스트 가이드 (Development Guide)

이 문서는 `ue4ss tool`을 로컬 환경에서 빌드, 실행, 테스트하고 기능을 확장하려는 개발자를 위한 가이드입니다.

---

## 1. 개발 환경 요구 사항

* **OS**: Windows 10 버전 1809 이상 또는 Windows 11 (x64)
* **SDK**: [.NET 10 SDK](https://dotnet.microsoft.com/download)
* **IDE 권장**: Visual Studio 2026 / 2022 최신 버전 또는 VS Code (C# Dev Kit 확장)

---

## 2. 빌드 및 실행

### 솔루션 빌드
```powershell
dotnet build "ue4ss tool.slnx"
```

### 애플리케이션 실행
```powershell
dotnet run --project "ue4ss tool/ue4ss tool.csproj"
```

---

## 3. 단위 테스트 (xUnit)

`Ue4ssTool.Tests` 프로젝트는 총 92개의 단위 테스트를 포함하고 있으며, 실제 게임 설치본 없이도 모의 파일 시스템(`FakeLibrary`)을 통해 모든 감지 및 설치 로직을 신속하고 안전하게 검증합니다.

### 전체 테스트 실행
```powershell
dotnet test "ue4ss tool.slnx"
```

### 테스트 스위트 구조

| 테스트 파일 | 검증 항목 |
|---|---|
| [FakeLibrary.cs](file:///h:/source/repos/ue4ss%20tool/Ue4ssTool.Tests/FakeLibrary.cs) | 임시 폴더에 Steam 라이브러리(`steamapps/common`, `libraryfolders.vdf`, `appmanifest`) 및 가상 게임 트리(UE4/5/3)를 생성하는 픽스처 |
| [DetectionTests.cs](file:///h:/source/repos/ue4ss%20tool/Ue4ssTool.Tests/DetectionTests.cs) | 프로젝트 루트 탐색, Shipping exe 우선순위 판별, IoStore 및 안티치트 감지 |
| [EngineVersionTests.cs](file:///h:/source/repos/ue4ss%20tool/Ue4ssTool.Tests/EngineVersionTests.cs) | `FILEVERSION` 파싱, 4MB 청크 및 64B 오버랩 바이트 스트림 스캔, 손상된 바이너리 예외 처리 |
| [InstallerTests.cs](file:///h:/source/repos/ue4ss%20tool/Ue4ssTool.Tests/InstallerTests.cs) | Zip 분석, Preflight 충돌 검증, 업데이트 시 사용자 모드 보존, 비활성화 토글, 휴지통 삭제 검증 |
| [Ue4ssSettingsTests.cs](file:///h:/source/repos/ue4ss%20tool/Ue4ssTool.Tests/Ue4ssSettingsTests.cs) | ini 파일의 BOM 보존, 줄바꿈 유지, `[Debug]` 섹션 내 콘솔 설정 강제 변경 |
| [Ue4ssStateTests.cs](file:///h:/source/repos/ue4ss%20tool/Ue4ssTool.Tests/Ue4ssStateTests.cs) | `dwmapi.dll` 제품명 검사, Subfolder vs Flat 레이아웃 판별, `.disabled` 상태 식별 |
| [ReleaseTests.cs](file:///h:/source/repos/ue4ss%20tool/Ue4ssTool.Tests/ReleaseTests.cs) | GitHub 릴리스 JSON 파싱 및 패키지 식별 로직 검증 |

---

## 4. Avalonia UI 개발 시 유의사항

* **컴파일 바인딩 (`AvaloniaUseCompiledBindingsByDefault`)**:
  * [ue4ss tool.csproj](file:///h:/source/repos/ue4ss%20tool/ue4ss%20tool/ue4ss%20tool.csproj)에 `<AvaloniaUseCompiledBindingsByDefault>true</AvaloniaUseCompiledBindingsByDefault>`가 켜져 있습니다.
  * XAML에서 데이터 바인딩 시 `x:DataType`을 명시해야 컴파일 타임에 유효성이 검증됩니다.
* **UI 스레드와 비동기 작업**:
  * 디렉터리 스캔이나 대용량 바이너리 바이트 스트림 탐색은 백그라운드 스레드(`Task.Run`)에서 수행해야 UI가 멈추지 않습니다.
  * 진행 상태 업데이트는 반드시 `Dispatcher.UIThread.Post(...)`를 통해 메인 스레드로 디스패치합니다.
* **Internal 멤버 접근**:
  * 핵심 로직(`UnrealInspector`, `Ue4ssInstaller` 등)의 많은 내부 헬퍼는 `internal`로 선언되어 있으며, `[assembly: InternalsVisibleTo("Ue4ssTool.Tests")]`를 통해 테스트 프로젝트에서 직접 접근하여 검증할 수 있습니다.
