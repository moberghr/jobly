/**
 * Exposes shared dependencies on window.Warp for extension JS modules.
 * Extensions access React, ReactDOM, UI components, and the API client
 * without bundling their own copies.
 */
import React from 'react';
import ReactDOM from 'react-dom/client';
import api from '@/api/client';
import { Card, CardContent, CardHeader, CardTitle, CardDescription, CardFooter } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';

declare global {
  interface Window {
    Warp?: WarpSDK;
  }
}

export interface WarpSDK {
  React: typeof React;
  ReactDOM: typeof ReactDOM;
  api: typeof api;
  components: {
    Card: typeof Card;
    CardContent: typeof CardContent;
    CardHeader: typeof CardHeader;
    CardTitle: typeof CardTitle;
    CardDescription: typeof CardDescription;
    CardFooter: typeof CardFooter;
    Button: typeof Button;
    Badge: typeof Badge;
  };
}

export function initSDK(): void {
  window.Warp = {
    React,
    ReactDOM,
    api,
    components: {
      Card,
      CardContent,
      CardHeader,
      CardTitle,
      CardDescription,
      CardFooter,
      Button,
      Badge,
    },
  };
}
