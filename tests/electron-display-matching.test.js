'use strict';

const assert = require('assert');
const {
  resolveRecordingDisplay,
  shouldMinimizeForRecording
} = require('../electron/display-matching');

const primary = {
  id: 10,
  label: 'Acer VG240Y',
  bounds: { x: 0, y: 0, width: 1920, height: 1080 },
  scaleFactor: 1
};
const recording = {
  id: 20,
  label: '4K Monitor',
  bounds: { x: 1920, y: -375, width: 2560, height: 1440 },
  scaleFactor: 1.5
};
const displays = [primary, recording];

const resolvedByLabel = resolveRecordingDisplay(displays, primary, {
  monitorIndex: 1,
  friendlyName: '4K Monitor',
  left: 1920,
  top: -563,
  width: 3840,
  height: 2160
});
assert.strictEqual(resolvedByLabel.id, recording.id);
assert.strictEqual(shouldMinimizeForRecording(primary, resolvedByLabel), false);
assert.strictEqual(shouldMinimizeForRecording(recording, resolvedByLabel), true);

const resolvedPrimary = resolveRecordingDisplay(displays, primary, {
  monitorIndex: 0,
  isPrimary: true
});
assert.strictEqual(resolvedPrimary.id, primary.id);

const unresolved = resolveRecordingDisplay(displays, primary, { monitorIndex: 99 });
assert.strictEqual(unresolved, null);
assert.strictEqual(shouldMinimizeForRecording(primary, unresolved), false);

console.log('Electron display matching checks passed.');
