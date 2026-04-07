import React from 'react';
import useBaseUrl from '@docusaurus/useBaseUrl';

interface ScreenshotProps {
  light: string;
  dark: string;
  alt: string;
}

export default function Screenshot({light, dark, alt}: ScreenshotProps) {
  const lightSrc = useBaseUrl(light);
  const darkSrc = useBaseUrl(dark);
  return (
    <>
      <img
        src={lightSrc}
        alt={alt}
        style={{width: '100%'}}
        data-theme-target="light"
      />
      <img
        src={darkSrc}
        alt={alt}
        style={{width: '100%'}}
        data-theme-target="dark"
      />
    </>
  );
}
