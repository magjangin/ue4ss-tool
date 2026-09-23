# ue4ss tool 개발 및 사용 문서

이 문서는 **`ue4ss tool`**의 구조, 언리얼 엔진 판별 메커니즘, UE4SS 모드 로더 수명주기 관리 및 개발 가이드를 상세히 설명합니다.

---

## 📚 목차

| 문서 | 설명 | 추천 대상 |
|---|---|---|
| **[개요 (Overview)](overview.md)** | 프로젝트 목적, 해결하는 페인포인트, 핵심 기능 및 기술 스택 | 모든 사용자 / 개발자 |
| **[시스템 아키텍처 (Architecture)](architecture.md)** | 전체 계층 구조, 데이터 파이프라인(스캔 및 설치), 모듈별 책임 분리 | 개발자 |
| **[언리얼 엔진 판별 (Engine Detection)](engine-detection.md)** | 실제 게임 exe 탐색 알고리즘, 바이너리 버전/시그니처 분석, 안티치트 감지 | 엔진 분석가 / 모더 |
| **[UE4SS 생명주기 관리 (UE4SS Lifecycle)](ue4ss-lifecycle.md)** | 패키지 레이아웃(Subfolder vs Flat), 설정 자동 최적화, 설치·업데이트·제거·비활성화 | 모더 / 도구 유지보수자 |
| **[개발 및 테스트 가이드 (Development Guide)](development-guide.md)** | 빌드 환경 구축, xUnit 테스트(92개) 구조 및 모의 라이브러리(`FakeLibrary`), 기여 가이드 | 개발자 |

---

## 🧭 추천 읽기 경로

* **UE4SS 모딩과 설치 흐름을 이해하고 싶은 경우**:
  [개요](overview.md) ➔ [UE4SS 생명주기 관리](ue4ss-lifecycle.md)
* **게임 바이너리 및 엔진 버전 탐지 로직이 궁금한 경우**:
  [언리얼 엔진 판별](engine-detection.md)
* **코드 수정 및 기능 확장을 준비하는 개발자의 경우**:
  [시스템 아키텍처](architecture.md) ➔ [개발 및 테스트 가이드](development-guide.md)
