# UE4SS 생명주기 관리 (UE4SS Lifecycle)

`ue4ss tool`은 모드 로더의 릴리스 다운로드부터 설치, 설정 파일 패치, 업데이트, 비활성화, 안전 제거까지 전체 생명주기(Lifecycle)를 안전하게 관리합니다.

---

## 1. 릴리스 탐색 및 다운로드 캐싱 (`Ue4ssReleases`)

* **GitHub REST API 연동**:
  * 공식 RE-UE4SS 리포지토리(`https://api.github.com/repos/UE4SS-RE/RE-UE4SS/releases`)에서 최신 안정판과 실험판 태그를 조회합니다.
  * 네트워크 장애나 오프라인 상태에서도 기존에 받아둔 패키지를 즉시 사용할 수 있도록 설계되었습니다.
* **로컬 캐시 경로**:
  ```
  %LOCALAPPDATA%\ue4ss tool\packages\
  ```
  이미 다운로드된 zip 파일은 다시 내려받지 않고 로컬 캐시를 재활용합니다.

### 지원 엔진별 패키지 매트릭스

| 엔진 버전 | 안정판 (v3.0.1) | 최신 실험판 (experimental-latest) | 권장 선택 |
|---|:---:|:---:|:---:|
| **UE 4.7 ~ 4.10** | ✗ | **✓** | 실험판 |
| **UE 4.11 ~ 5.3** | **✓** | **✓** | 실험판 또는 안정판 |
| **UE 5.4 ~ 5.8** | ✗ | **✓** | 실험판 |
| **개발/테스트 빌드 (Non-Shipping)** | ✗ | **✓** | 실험판 |

---

## 2. 패키지 레이아웃 (Layouts)

UE4SS 릴리스 버전에 따라 압축 파일 내부 및 설치 폴더 내 구조가 다릅니다:

```
[최신 실험판 - Subfolder 레이아웃]
<GameExeDir>/
  ├── <Game>-Win64-Shipping.exe
  ├── dwmapi.dll                     # 주입 프록시 DLL
  └── ue4ss/
      ├── UE4SS.dll                  # 코어 엔진
      ├── UE4SS-settings.ini         # 설정 파일
      └── Mods/                      # 모드 디렉터리
          ├── mods.txt
          └── ...

[구 안정판 - Flat 레이아웃]
<GameExeDir>/
  ├── <Game>-Win64-Shipping.exe
  ├── dwmapi.dll                     # 주입 프록시 DLL
  ├── UE4SS.dll                      # exe 옆에 공존
  ├── UE4SS-settings.ini             # exe 옆에 공존
  └── Mods/                          # exe 옆에 공존
```

---

## 3. 설치 사전 검증 (`Preflight`)

설치를 진행하기 전, 기존 게임 파일이나 설정이 망가지지 않도록 [Ue4ssInstaller.Preflight](file:///h:/source/repos/ue4ss%20tool/ue4ss%20tool/Ue4ssInstaller.cs)에서 충돌을 검사합니다. 충돌이 감지되면 `InstallBlockedException`을 던져 작업을 중단합니다:

1. **게임 실행 여부 검사**:
   * 게임 exe가 실행 중이면 DLL 교체 시 `Access Denied`가 발생하므로 즉시 차단합니다.
2. **배치 혼합 방지**:
   * 이미 Flat 레이아웃으로 설치된 곳에 Subfolder 패키지를 설치하거나, 그 반대의 경우 두 배치가 섞여 크래시가 나므로 설치를 막고 기존 설치 제거를 유도합니다.
3. **타 모드 프록시 DLL 보호**:
   * 폴더에 이미 `dwmapi.dll`이 존재하지만 제품명이 `UE4SS Injection Proxy`가 아닌 경우(예: ReShade, 기타 서드파티 래퍼), 중요한 외부 파일을 덮어쓰지 않도록 차단합니다.
4. **Path Traversal 방지**:
   * zip 압축 파일의 상대 경로가 설치 폴더 상위(`..`)를 벗어나는 악의적인 경로인지 검사합니다.

---

## 4. 디버그 및 GUI 콘솔 자동 패치 (`Ue4ssSettings`)

RE-UE4SS의 기본 릴리스 패키지는 콘솔 창이 꺼져 있어 모드 로딩 상태를 확인하기 어렵습니다. 도구는 설치 및 업데이트 시 [Ue4ssSettings.cs](file:///h:/source/repos/ue4ss%20tool/ue4ss%20tool/Ue4ssSettings.cs)를 통해 설정을 자동으로 활성화합니다.

* **타깃 키 (`ConsoleKeys`)**:
  ```ini
  [Debug]
  ConsoleEnabled = 1
  GuiConsoleEnabled = 1
  GuiConsoleVisible = 1
  ```
* **비파괴적 파싱 (Non-destructive patching)**:
  * UTF-8 BOM 유무를 자동으로 감지하여 보존합니다.
  * 기존 줄바꿈(`\r\n` 또는 `\n`)을 그대로 유지합니다.
  * 주석(`;`, `#`)이나 다른 설정값은 절대 건드리지 않고, 위의 3개 키만 `1`로 치환합니다.
  * `[Debug]` 섹션이나 해당 키가 없으면 자동으로 섹션 및 키를 삽입합니다.

---

## 5. 업데이트 및 모드 보존 메커니즘

새 버전의 UE4SS로 업데이트할 때 사용자가 직접 제작하거나 추가한 모드가 날아가지 않도록 보호합니다:
* **보존 대상 파일 (`UserFiles`)**:
  * `UE4SS-settings.ini` (단, 콘솔 활성화 3개 설정은 1로 자동 보정)
  * `Mods/mods.txt`
  * `Mods/mods.json`
  * 패키지 순정에 포함되지 않은 사용자의 **커스텀 모드 하위 폴더**
* 사용자가 확인 창(`ConfirmDialog`)에서 **"기존 설정 파일 및 모드 목록 유지"** 체크박스를 해제하면 순정 기본 파일로 초기화할 수도 있습니다.

---

## 6. 비활성화 (끄기/켜기)

게임을 순정 상태로 실행하고 싶을 때 모드를 완전히 삭제할 필요 없이 원클릭으로 주입을 중단할 수 있습니다:
* **끄기 (Disable)**:
  `dwmapi.dll` ➔ `dwmapi.dll.disabled` 로 이름 변경.
  Windows 런처는 더 이상 프록시 DLL을 로드하지 않으므로 순정 상태로 실행됩니다.
* **켜기 (Enable)**:
  `dwmapi.dll.disabled` ➔ `dwmapi.dll` 로 복원.

---

## 7. 안전 제거 (휴지통 이동)

[Ue4ssInstaller.Uninstall](file:///h:/source/repos/ue4ss%20tool/ue4ss%20tool/Ue4ssInstaller.cs)은 `File.Delete`로 영구 삭제하지 않고, **Windows 휴지통(Recycle Bin)**으로 항목을 보냅니다:

* **Subfolder 레이아웃**:
  * `dwmapi.dll`과 `ue4ss\` 폴더만 휴지통으로 이동.
* **Flat 레이아웃**:
  * 게임 파일과 한 폴더에 공존하므로 엄격한 화이트리스트 검사를 적용합니다:
    * 확정 항목: `UE4SS.dll`, `UE4SS.pdb`, `UE4SS-settings.ini`, `UE4SS.log`, `Mods/`, `UE4SS_Signatures`, `MemberVariableLayout.ini`, `VTableLayout.ini`, `UE4SS_ObjectDump.txt`
    * 조건부 항목 (`MarkedFlatItems`): `README.md`, `Changelog.md`, `API.txt`, `LICENSE` 등은 **파일 내용 앞부분에 "UE4SS"라는 단어가 포함된 경우에만** 삭제.
* 게임 실행에 필요한 본체 바이너리와 엔진 에셋은 절대 건드리지 않습니다.
