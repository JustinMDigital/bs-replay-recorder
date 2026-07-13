'use strict';

function normalizeLabel(value) {
  return String(value || '').trim().toLowerCase();
}

function resolveRecordingDisplay(displays, primaryDisplay, target = {}) {
  if (!Array.isArray(displays) || displays.length === 0 || !target) return null;

  if (target.isPrimary && primaryDisplay) return primaryDisplay;

  const targetLabels = [target.friendlyName, target.label]
    .map(normalizeLabel)
    .filter(Boolean);
  if (targetLabels.length) {
    const labelMatch = displays.find(display => {
      const label = normalizeLabel(display?.label);
      return label && targetLabels.some(targetLabel =>
        label === targetLabel || label.includes(targetLabel) || targetLabel.includes(label));
    });
    if (labelMatch) return labelMatch;
  }

  const targetLeft = Number(target.left);
  const targetTop = Number(target.top);
  const targetWidth = Number(target.width);
  const targetHeight = Number(target.height);
  if ([targetLeft, targetTop, targetWidth, targetHeight].every(Number.isFinite)) {
    const geometryMatch = displays
      .map(display => ({ display, score: scoreDisplayGeometry(display, targetLeft, targetTop, targetWidth, targetHeight) }))
      .sort((left, right) => right.score - left.score)[0];
    if (geometryMatch?.score >= 6) return geometryMatch.display;
  }

  const monitorIndex = Number(target.monitorIndex);
  return Number.isInteger(monitorIndex) && monitorIndex >= 0 && monitorIndex < displays.length
    ? displays[monitorIndex]
    : null;
}

function scoreDisplayGeometry(display, left, top, width, height) {
  const bounds = display?.bounds || {};
  const scaleFactor = Number(display?.scaleFactor) || 1;
  const physicalWidth = Number(bounds.width) * scaleFactor;
  const physicalHeight = Number(bounds.height) * scaleFactor;
  let score = 0;

  if (Number(bounds.x) === left) score += 3;
  if (Number(bounds.y) === top) score += 3;
  if (Number(bounds.width) === width || Math.round(physicalWidth) === width) score += 2;
  if (Number(bounds.height) === height || Math.round(physicalHeight) === height) score += 2;
  return score;
}

function shouldMinimizeForRecording(currentDisplay, recordingDisplay) {
  return currentDisplay != null &&
    recordingDisplay != null &&
    String(currentDisplay.id) === String(recordingDisplay.id);
}

module.exports = {
  resolveRecordingDisplay,
  shouldMinimizeForRecording
};
