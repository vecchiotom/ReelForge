'use client';
import { Component, type ReactNode } from 'react';

interface SceneBoundaryProps {
  children: ReactNode;
  fallback: ReactNode;
}

interface SceneBoundaryState {
  hasError: boolean;
}

// Wraps a WebGL canvas so a context failure (old GPU, headless browser,
// exhausted context pool) degrades to a static fallback instead of crashing
// or blanking the hero.
export class SceneBoundary extends Component<SceneBoundaryProps, SceneBoundaryState> {
  state: SceneBoundaryState = { hasError: false };

  static getDerivedStateFromError() {
    return { hasError: true };
  }

  render() {
    return this.state.hasError ? this.props.fallback : this.props.children;
  }
}
