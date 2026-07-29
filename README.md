# Background Studio · C# WPF

Windows에서 이미지와 동영상 배경을 로컬로 제거하고 편집하는 .NET 10 WPF
앱입니다. 파일은 외부 AI API로 전송하지 않습니다.

## 기능

- U2NetP ONNX 모델을 앱에서 내려받고 MD5 체크섬 검증
- 이미지 배경 제거와 PNG/JPEG/BMP/TIFF/SVG 저장
- 단색·다른 이미지·원본 블러 배경 합성
- 마스크 기준과 가장자리 부드러움 조정
- 그림자 흐림·불투명도·오프셋
- 중앙 정렬·크기·X/Y 위치, 7종 필터, 마스크·외곽선 단독 레이어
- SHA-256 검증을 거친 FFmpeg 8.1.2 앱 내부 자동 준비
- 투명 WebM/MOV 또는 MP4/WebM/MOV/GIF
- 취소, 진행률, 모델/FFmpeg 누락 상태, 키보드 포커스

## 실행

Windows 10/11과 .NET 10 SDK가 필요합니다.

```powershell
dotnet restore BackgroundStudio.csproj
dotnet run --project BackgroundStudio.csproj
```

첫 화면의 `모델 준비`를 누르면 rembg가 배포하는 U2NetP 모델 약 5MB를
`%LOCALAPPDATA%\BackgroundStudio\models\`에 저장합니다.

동영상에서는 상단 `FFmpeg 준비`를 누르거나 바로 처리를 시작하면 검증된
Essentials ZIP을 `%LOCALAPPDATA%\BackgroundStudio\ffmpeg\`에 내려받습니다.
시스템 PATH 설정이나 관리자 권한은 필요하지 않습니다.

## 사용 순서

1. 이미지 또는 동영상을 엽니다.
2. 투명·단색·다른 이미지·블러 중 결과 배경 하나를 고릅니다.
3. 필터, 출력 레이어, 중앙 정렬, 크기와 위치를 조절합니다.
4. `배경 제거 시작`을 누릅니다.
5. 이미지는 PNG/JPEG/BMP/TIFF/SVG, 동영상은 MP4/WebM/MOV/GIF로 저장합니다.

## 빌드와 테스트

```powershell
dotnet build BackgroundStudio.csproj --configuration Release
dotnet test BackgroundStudio.Tests.csproj --configuration Release
```

## 현실적인 제한

- 복잡한 머리카락, 유리, 연기, 모션 블러는 수동 보정이 필요할 수 있습니다.
- 프레임별 배경 제거는 긴 동영상에서 느립니다. 먼저 짧은 구간으로 품질을
  확인하세요.
- 투명 WebM은 재생 프로그램에 따라 알파가 검게 보일 수 있습니다.
- 이 앱은 Windows 전용입니다. 서버 자동화는 별도 Python 저장소를,
  설치 없는 사용은 별도 GitHub Pages 저장소를 사용하세요.

외부 구성요소는 [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md), 디자인
판단은 [`docs/design-direction.md`](docs/design-direction.md)에 정리했습니다.
전문 편집값은 [`docs/pro-editing-guide.md`](docs/pro-editing-guide.md)를
확인하세요.

## License

앱 코드는 MIT입니다. 모델과 FFmpeg는 각자의 조건을 따릅니다.
