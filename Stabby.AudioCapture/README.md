// Stabby.AudioCapture - README (개발 진행 문서)
# Stabby.AudioCapture — Phase 1 구현물

## 현재 상태 (2026-09-16)
plan.md의 **Phase 1 (C++ Audio Capture DLL)** 코드 스캐폴드 완료:
- `LoopbackCapture.h/cpp` — WASAPI Process Loopback 캡처 코어
- `exports.cpp` — C Export (P/Invoke 서명과 1:1 대응)
- `dllmain.cpp` — DLL 엔트리 포인트
- `Stabby.AudioCapture.vcxproj` — MSBuild 프로젝트 (VS2022 v143, x64)

## 중요 정보 — 기존 DLL과의 관계
- **기존 서드파티 ProcessAudioCapture.dll**은 MIT 세르비 프로젝트로, 기존 P/Invoke (`AudioCaptureNative.cs`)는 그 DLL이 기대하는 시그니처를 이미 따름.
- 이 프로젝트가 완성되면 (빌드 후 `ProcessAudioCapture.dll`로 생성/교체), **C# 쪽 수정 없이 즉시 교체 가능** — 같은 심볼을 export하도록 설계함.
- 이미 plan.md에서 말한 것처럼, 외부 의존성 감소 + 코드 커스터마이징은 Phase 1 이득.

## 빌드 방법 (Windows, VS2022)
```
# Developer Prompt에서
cd Stabby.AudioCapture
msbuild Stabby.AudioCapture.vcxproj /p:Configuration=Release /p:Platform=x64
# 결과: x64/Release/ProcessAudioCapture.dll (또는 Stabby.AudioCapture.dll)
```

## Windows에서 빌드되면 검증해야 할 것 (Phase 1 완료 조건)
1. `PacIsSupported()`가 Windows 10 2004+ 에서 1 반환
2. `PacStartCapture()` → `PacIsCapturing()` 1 확인
3. 선택한 프로세스 오디오 녹음 → WAV 파일 44byte WAV 헤더가 실제 사이즈로 패치되었는지 확인
4. `PacPauseCapture/Resume` → pause 동안 silent
5. `PacStopCapture()` → 파일 finalize 확인

## ⚠ 빌드 후 § C# 프로젝트의 bin/에 DLL 복사 필요
```
copy x64\Release\ProcessAudioCapture.dll ..\Stabby\bin\x64\Release\net8.0\ProcessAudioCapture.dll
```
(또는 .csproj에 Content Include로 자동 복사 설정)

## 구현 시 주의 사항 (중요)
-칼kback event handler(`IActivateAudioInterfaceCompletionHandler`)가 COM 기반 — 필요 시 `wrl/implements` RuntimeClass로 정의했으나 현재 stub, 실제 동작 시 event-handler 상태 체크 필수
- `PROCESS_LOOPBACK_MODE_EXCLUDE_TARGET_PROCESS_TREE`는 다른 모든 오디오 캡처 — 이 모드는 단독(`Exclude` 한 개)만 유효, 여러개 동시 불가
- DRM (Widevine 등) 보호된 오디오는 무음 (plan.md limitation)
- 임시 WAV는 PCM only (float32 mixformat 가능 — ffmpeg에서 잘 변환됨)
