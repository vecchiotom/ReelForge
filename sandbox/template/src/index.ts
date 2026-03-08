import {registerRoot} from 'remotion';
// Explicitly specify the .tsx extension to avoid resolution attempts against
// `root.js` which can occur when the project workspace is mutated by agents.
import { Root } from './root.tsx';

registerRoot(Root);
