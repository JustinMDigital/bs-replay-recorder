let state = null;
let toastTimeout = null;
let selectedReplayFiles = [];
let selectedCollectionReplayFiles = [];
let editingQueueId = null;
let selectedQueueId = null;
let queueSearchText = '';
let isRendering = false;
let settingsDirty = false;
let activeView = 'run';
let setupAssistantHidden = readSetupAssistantHidden();
let displayInfo = { displays: [] };
let draggedQueueId = null;
let pendingSetupEnabledInstanceCount = null;
let pendingSetupInstanceCreation = null;
let lastRunPlanPlayheadLeftPx = null;
let runPlanPlayheadInstantTimeout = null;
let shutdownModalVisible = false;
let colorPresetCatalog = { builtIn: [], beatSaber: [], saved: [] };
let setupSourcePathInfo = null;
let queueExportLastFocus = null;
let queueExportContext = null;
let recordingRenameLastFocus = null;
let recordingRenameContext = null;
let mapCardExportLastFocus = null;
let mapCardExportData = null;
let mapCardCategoriesDirty = false;
let mapCardCategoriesEnabled = readMapCardCategoriesEnabled();
let selectedBenchmarkConcurrencies = null;
let selectedCollectionId = null;
let recordingNameFormat = 'Default';
let runPlanWorkerActionPending = false;
let runPlanWorkerActionLockedUntil = 0;
let runPlanWorkerActionUnlockTimeout = null;
let activeInstanceCountActionPending = false;
let pendingActiveInstanceCount = null;

const runPlanTimingDefaults = Object.freeze({
  startLeadInSeconds: 3,
  recorderStartupSeconds: 5,
  syncMarkerSeconds: 0.95,
  recorderFinalizationSeconds: 25,
  interReplayGapSeconds: 5
});

const defaultLaunchArguments = '-screen-fullscreen 0 -screen-width 1920 -screen-height 1080 --no-yeet fpfc --verbose';
const windowed720pLaunchArguments = '-screen-fullscreen 0 -screen-width 1280 -screen-height 720 --no-yeet fpfc --verbose';
const windowed1440pLaunchArguments = '-screen-fullscreen 0 -screen-width 2560 -screen-height 1440 --no-yeet fpfc --verbose';
const windowed4kLaunchArguments = '-screen-fullscreen 0 -screen-width 3840 -screen-height 2160 --no-yeet fpfc --verbose';
const windowed5kLaunchArguments = '-screen-fullscreen 0 -screen-width 5120 -screen-height 2880 --no-yeet fpfc --verbose';
const recordingRenameFallbackExamples = Object.freeze({
  Default: '001 - Song [Difficulty]',
  Key: '4fc4b',
  KeySong: '4fc4b - Song',
  Song: 'Song',
  SongArtist: 'Song - Artist',
  SongArtistPlayer: 'Song - Artist - Player',
  SongPlayer: 'Song - Player',
  SongMapper: 'Song - Mapper',
  SongDifficulty: 'Song - Expert+',
  PlayerSong: 'Player - Song'
});
const minManagedInstanceCount = 1;
const maxManagedInstanceCount = 4;
const visibleManagedInstanceSlots = maxManagedInstanceCount;
const managedInstanceNamePrefix = 'I-';
const defaultCaptureEngine = 'FFmpegDdagrab';
const gamePresentationDefaults = Object.freeze({
  noHud: true,
  loadPlayerEnvironment: false,
  loadPlayerJumpDistance: false,
  overrideReplayPlayerSettings: false,
  ignoreModifiers: false,
  applyJdFixerSettings: false,
  jdFixerMode: 'ReactionTime',
  jdFixerJumpDistance: 18,
  jdFixerReactionTime: 450,
  showHead: false,
  showLeftSaber: true,
  showRightSaber: true,
  showWatermark: true,
  showTimelineMisses: true,
  showTimelineBombs: true,
  showTimelinePauses: true,
  sfxVolume: 0.3,
  noTextsAndHuds: true,
  advancedHud: false,
  reduceDebris: true,
  noFailEffects: false,
  saberTrailIntensity: 0,
  leftSaberColor: '#a82020',
  rightSaberColor: '#2064a8',
  lightColorA: '#ff3030',
  lightColorB: '#c03030',
  boostLightColorA: '#ff3030',
  boostLightColorB: '#c03030',
  wallColor: '#3098ff',
  noteJumpDurationType: 'Dynamic',
  noteJumpFixedDuration: 0.2,
  noteJumpStartBeatOffset: 0,
  hideNoteSpawnEffect: false,
  adaptiveSfx: true,
  arcsHapticFeedback: true,
  arcVisibility: 'Low',
  environmentEffectsFilterDefaultPreset: 'AllEffects',
  environmentEffectsFilterExpertPlusPreset: 'AllEffects',
  headsetHapticIntensity: 0.7
});
const liveGamePresentationFields = new Set([
  'noHud',
  'loadPlayerEnvironment',
  'loadPlayerJumpDistance',
  'overrideReplayPlayerSettings',
  'ignoreModifiers',
  'applyJdFixerSettings',
  'jdFixerMode',
  'jdFixerJumpDistance',
  'jdFixerReactionTime',
  'showHead',
  'showLeftSaber',
  'showRightSaber',
  'showWatermark',
  'showTimelineMisses',
  'showTimelineBombs',
  'showTimelinePauses',
  'sfxVolume'
]);
const liveSyncedGamePresentationFields = new Set([
  ...liveGamePresentationFields,
  'leftSaberColor',
  'rightSaberColor',
  'lightColorA',
  'lightColorB',
  'boostLightColorA',
  'boostLightColorB',
  'wallColor'
]);
const restartRecommendedGamePresentationFields = new Set([
  'noTextsAndHuds',
  'advancedHud',
  'reduceDebris',
  'noFailEffects',
  'saberTrailIntensity',
  'noteJumpDurationType',
  'noteJumpFixedDuration',
  'noteJumpStartBeatOffset',
  'hideNoteSpawnEffect',
  'adaptiveSfx',
  'arcsHapticFeedback',
  'arcVisibility',
  'environmentEffectsFilterDefaultPreset',
  'environmentEffectsFilterExpertPlusPreset',
  'headsetHapticIntensity'
]);
const restartRecommendedSettings = Object.freeze({
  beatSaberLaunchPreset: 'launch settings',
  beatSaberLaunchArguments: 'launch settings',
  shareCustomSabers: 'custom saber sharing',
  sharedCustomSabersDirectory: 'custom saber folder',
  shareCustomNotes: 'custom note sharing',
  sharedCustomNotesDirectory: 'custom note folder',
  shareCustomPlatforms: 'custom platform sharing',
  sharedCustomPlatformsDirectory: 'custom platform folder',
  shareCustomAvatars: 'custom avatar sharing',
  sharedCustomAvatarsDirectory: 'custom avatar folder',
  shareCustomWalls: 'custom wall sharing',
  sharedCustomWallsDirectory: 'custom wall folder',
  shareCustomBombs: 'custom bomb sharing',
  sharedCustomBombsDirectory: 'custom bomb folder'
});
const colorFieldIds = [
  'leftSaberColor',
  'rightSaberColor',
  'lightColorA',
  'lightColorB',
  'boostLightColorA',
  'boostLightColorB',
  'wallColor'
];

const mapCardFontFamily = '"SF Pro Display", -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif';
const mapCardCategoryIcons = Object.freeze({
  standard: {
    width: 12,
    height: 12,
    path: 'M7.08334 7.11179C7.02572 7.21301 6.95602 7.29897 6.87423 7.36967C6.79244 7.44038 6.69173 7.49194 6.57209 7.52435L1.09234 9.02861C0.873948 9.08856 0.658371 9.06234 0.445612 8.94994C0.232853 8.83754 0.0941689 8.66211 0.0295605 8.42364C-0.0303918 8.20525 0.000848882 7.98851 0.123283 7.77342C0.245716 7.55833 0.416131 7.42081 0.634527 7.36086L5.26142 6.07471L4.00505 1.43963C3.9451 1.22124 3.97158 1.00581 4.08448 0.793334C4.19738 0.580863 4.37256 0.442035 4.61002 0.376851C4.82841 0.316898 5.04516 0.348138 5.26024 0.470572C5.47533 0.593005 5.61285 0.763421 5.6728 0.981817L7.17706 6.46157C7.20976 6.58069 7.21685 6.69361 7.19832 6.80033C7.17979 6.90704 7.14146 7.01086 7.08334 7.11179ZM11.6381 9.7045C11.5805 9.80572 11.5108 9.89168 11.429 9.96239C11.3472 10.0331 11.2465 10.0846 11.1269 10.1171L5.64713 11.6213C5.42874 11.6813 5.21341 11.6552 5.00116 11.5431C4.78891 11.431 4.64997 11.2554 4.58435 11.0164C4.5244 10.798 4.55564 10.5812 4.67807 10.3661C4.8005 10.151 4.97092 10.0135 5.18932 9.95357L9.81621 8.66742L8.55984 4.03235C8.49989 3.81395 8.52611 3.59837 8.63851 3.38561C8.75091 3.17286 8.92634 3.03417 9.16481 2.96956C9.3832 2.90961 9.59995 2.94085 9.81503 3.06328C10.0301 3.18572 10.1676 3.35613 10.2276 3.57453L11.7318 9.05428C11.7646 9.1734 11.7716 9.28632 11.7531 9.39304C11.7346 9.49975 11.6962 9.60358 11.6381 9.7045Z'
  },
  accuracy: {
    width: 18,
    height: 14,
    path: 'M8 2C7.73478 2 7.48043 2.10536 7.29289 2.29289C7.10536 2.48043 7 2.73478 7 3V11C7 11.2652 7.10536 11.5196 7.29289 11.7071C7.48043 11.8946 7.73478 12 8 12H10C10.2652 12 10.5196 11.8946 10.7071 11.7071C10.8946 11.5196 11 11.2652 11 11V3C11 2.73478 10.8946 2.48043 10.7071 2.29289C10.5196 2.10536 10.2652 2 10 2H8ZM13 6V3C13 2.20435 12.6839 1.44129 12.1213 0.87868C11.5587 0.31607 10.7956 0 10 0H8C7.20435 0 6.44129 0.31607 5.87868 0.87868C5.31607 1.44129 5 2.20435 5 3V6H1C0.447715 6 0 6.44772 0 7C0 7.55228 0.447715 8 1 8H5V11C5 11.7957 5.31607 12.5587 5.87868 13.1213C6.44129 13.6839 7.20435 14 8 14H10C10.7957 14 11.5587 13.6839 12.1213 13.1213C12.6839 12.5587 13 11.7957 13 11V8H17C17.5523 8 18 7.55228 18 7C18 6.44772 17.5523 6 17 6H13Z'
  },
  tech: {
    width: 14,
    height: 14,
    path: 'M7.28033 0.46967C7.57322 0.762563 7.57322 1.23744 7.28033 1.53033L5.947 2.86366C5.93297 2.87769 5.91853 2.89104 5.90371 2.90372C6.07498 3.24022 6.16667 3.61538 6.16667 4C6.16667 4.64094 5.91205 5.25563 5.45884 5.70884C5.00563 6.16205 4.39094 6.41667 3.75 6.41667C3.10906 6.41667 2.49437 6.16205 2.04116 5.70884C1.58795 5.25563 1.33333 4.64094 1.33333 4C1.33333 3.35906 1.58795 2.74437 2.04116 2.29116C2.49437 1.83795 3.10906 1.58333 3.75 1.58333C4.13462 1.58333 4.50978 1.67502 4.84628 1.84629C4.85896 1.83147 4.87231 1.81703 4.88634 1.803L6.21967 0.46967C6.51256 0.176777 6.98744 0.176777 7.28033 0.46967ZM9.37449 0.957825C9.82771 0.504612 10.4424 0.25 11.0833 0.25C11.7243 0.25 12.339 0.504613 12.7922 0.957825C13.2454 1.41104 13.5 2.02573 13.5 2.66667C13.5 3.30761 13.2454 3.9223 12.7922 4.37551C12.339 4.82872 11.7243 5.08333 11.0833 5.08333C10.6987 5.08333 10.3235 4.99165 9.98705 4.82038C9.97437 4.8352 9.96102 4.84964 9.947 4.86366L4.61366 10.197C4.59964 10.211 4.58519 10.2244 4.57037 10.2371C4.74164 10.5736 4.83333 10.9487 4.83333 11.3333C4.83333 11.9743 4.57872 12.589 4.12551 13.0422C3.6723 13.4954 3.05761 13.75 2.41667 13.75C1.77573 13.75 1.16104 13.4954 0.707825 13.0422C0.254613 12.589 0 11.9743 0 11.3333C0 10.6924 0.254612 10.0777 0.707825 9.62449C1.16104 9.17128 1.77573 8.91667 2.41667 8.91667C2.80129 8.91667 3.17645 9.00835 3.51295 9.17962C3.52563 9.1648 3.53898 9.15036 3.553 9.13634L8.88634 3.803C8.90036 3.78898 8.9148 3.77563 8.92962 3.76295C8.75835 3.42645 8.66667 3.05129 8.66667 2.66667C8.66667 2.02573 8.92128 1.41104 9.37449 0.957825ZM11.0833 1.75C10.8402 1.75 10.6071 1.84658 10.4352 2.01849C10.2632 2.19039 10.1667 2.42355 10.1667 2.66667C10.1667 2.90978 10.2632 3.14294 10.4352 3.31485C10.6071 3.48676 10.8402 3.58333 11.0833 3.58333C11.3264 3.58333 11.5596 3.48676 11.7315 3.31485C11.9034 3.14294 12 2.90978 12 2.66667C12 2.42355 11.9034 2.19039 11.7315 2.01849C11.5596 1.84658 11.3264 1.75 11.0833 1.75ZM3.75 3.08333C3.50688 3.08333 3.27373 3.17991 3.10182 3.35182C2.92991 3.52373 2.83333 3.75688 2.83333 4C2.83333 4.24312 2.92991 4.47627 3.10182 4.64818C3.27373 4.82009 3.50688 4.91667 3.75 4.91667C3.99312 4.91667 4.22627 4.82009 4.39818 4.64818C4.57009 4.47627 4.66667 4.24312 4.66667 4C4.66667 3.75688 4.57009 3.52373 4.39818 3.35182C4.22627 3.17991 3.99312 3.08333 3.75 3.08333ZM8.04116 8.29116C8.49437 7.83795 9.10906 7.58333 9.75 7.58333C10.3909 7.58333 11.0056 7.83795 11.4588 8.29116C11.9121 8.74437 12.1667 9.35906 12.1667 10C12.1667 10.6409 11.9121 11.2556 11.4588 11.7088C11.0056 12.1621 10.3909 12.4167 9.75 12.4167C9.36538 12.4167 8.99021 12.325 8.65372 12.1537C8.64104 12.1685 8.62769 12.183 8.61366 12.197L7.28033 13.5303C6.98744 13.8232 6.51256 13.8232 6.21967 13.5303C5.92678 13.2374 5.92678 12.7626 6.21967 12.4697L7.553 11.1363C7.56703 11.1223 7.58147 11.109 7.59629 11.0963C7.42502 10.7598 7.33333 10.3846 7.33333 10C7.33333 9.35906 7.58795 8.74437 8.04116 8.29116ZM9.75 9.08333C9.50688 9.08333 9.27373 9.17991 9.10182 9.35182C8.92991 9.52373 8.83333 9.75688 8.83333 10C8.83333 10.2431 8.92991 10.4763 9.10182 10.6482C9.27373 10.8201 9.50688 10.9167 9.75 10.9167C9.99312 10.9167 10.2263 10.8201 10.3982 10.6482C10.5701 10.4763 10.6667 10.2431 10.6667 10C10.6667 9.75688 10.5701 9.52373 10.3982 9.35182C10.2263 9.17991 9.99312 9.08333 9.75 9.08333ZM2.41667 10.4167C2.17355 10.4167 1.94039 10.5132 1.76849 10.6852C1.59658 10.8571 1.5 11.0902 1.5 11.3333C1.5 11.5764 1.59658 11.8096 1.76849 11.9815C1.94039 12.1534 2.17355 12.25 2.41667 12.25C2.65978 12.25 2.89294 12.1534 3.06485 11.9815C3.23676 11.8096 3.33333 11.5764 3.33333 11.3333C3.33333 11.0902 3.23676 10.8571 3.06485 10.6852C2.89294 10.5132 2.65978 10.4167 2.41667 10.4167Z'
  },
  speed: {
    width: 14,
    height: 14,
    path: 'M6.22222 10.8889C6.22222 11.3167 6.57222 11.6667 7 11.6667C7.42778 11.6667 7.77778 11.3167 7.77778 10.8889C7.77778 10.4611 7.42778 10.1111 7 10.1111C6.57222 10.1111 6.22222 10.4611 6.22222 10.8889ZM7 0C6.57045 0 6.22222 0.348223 6.22222 0.777778V2.33333C6.22222 2.76289 6.57045 3.11111 7 3.11111H7.0848C7.46752 3.11111 7.77778 2.80085 7.77778 2.41813C7.77778 1.99595 8.15436 1.669 8.55887 1.78985C10.8049 2.46087 12.4444 4.53255 12.4444 7C12.4444 10.01 10.01 12.4444 7 12.4444C3.99 12.4444 1.55556 10.01 1.55556 7C1.55556 6.08825 1.77898 5.22951 2.17562 4.47664C2.45063 3.95465 3.14541 3.92319 3.5626 4.34038L6.45207 7.22985C6.75468 7.53246 7.24532 7.53246 7.54793 7.22985C7.85086 6.92692 7.8505 6.43567 7.54712 6.13318L3.42058 2.01878C3.06877 1.668 2.50685 1.62343 2.14839 1.96742C0.824907 3.23746 0 5.01707 0 7C0 10.8656 3.12667 14 7 14C8.85652 14 10.637 13.2625 11.9497 11.9497C13.2625 10.637 14 8.85652 14 7C14 5.14348 13.2625 3.36301 11.9497 2.05025C10.637 0.737498 8.85652 2.76642e-08 7 0ZM11.6667 7C11.6667 6.57222 11.3167 6.22222 10.8889 6.22222C10.4611 6.22222 10.1111 6.57222 10.1111 7C10.1111 7.42778 10.4611 7.77778 10.8889 7.77778C11.3167 7.77778 11.6667 7.42778 11.6667 7ZM2.33333 7C2.33333 7.42778 2.68333 7.77778 3.11111 7.77778C3.53889 7.77778 3.88889 7.42778 3.88889 7C3.88889 6.57222 3.53889 6.22222 3.11111 6.22222C2.68333 6.22222 2.33333 6.57222 2.33333 7Z'
  },
  extreme: {
    width: 11,
    height: 14,
    path: 'M9.95495 6.37796C9.77427 6.14462 9.55431 5.94239 9.35006 5.74016C8.82373 5.27348 8.2267 4.93903 7.72393 4.44902C6.55343 3.31343 6.29419 1.43893 7.04048 0C6.29419 0.178894 5.64217 0.58335 5.08441 1.0267C3.04979 2.64452 2.2485 5.49905 3.2069 7.94912C3.23832 8.0269 3.26975 8.10468 3.26975 8.20579C3.26975 8.37691 3.15191 8.53247 2.9948 8.59469C2.81412 8.67247 2.62558 8.6258 2.47632 8.50135C2.42919 8.46246 2.39776 8.42357 2.36634 8.36913C1.47865 7.25687 1.33724 5.66238 1.93428 4.38679C0.622373 5.4446 -0.0924956 7.23354 0.00962853 8.92137C0.0567628 9.31027 0.103897 9.69917 0.237444 10.0881C0.347424 10.5547 0.559528 11.0214 0.795199 11.4337C1.64362 12.7793 3.11263 13.7437 4.69163 13.9382C6.37275 14.1482 8.17171 13.8448 9.46004 12.6937C10.8976 11.4025 11.4004 9.3336 10.662 7.56022L10.5598 7.35799C10.3949 7.0002 9.95495 6.37796 9.95495 6.37796ZM7.47255 11.2781C7.25259 11.4648 6.89123 11.667 6.60842 11.7448C5.72858 12.0559 4.84874 11.6203 4.33027 11.107C5.26509 10.8892 5.82285 10.2047 5.98782 9.51249C6.12137 8.89025 5.86998 8.37691 5.76786 7.778C5.67359 7.20243 5.6893 6.71241 5.90141 6.17573C6.05067 6.4713 6.20778 6.76686 6.39632 7.0002C7.00121 7.778 7.95175 8.12023 8.15599 9.17804C8.18742 9.28693 8.20313 9.39582 8.20313 9.51249C8.2267 10.1503 7.94389 10.8503 7.47255 11.2781Z'
  }
});

const mapCardCategories = Object.freeze([
  { id: 'standard', name: 'Standard', color: '#ffc700', icon: 'standard' },
  { id: 'accuracy', name: 'Accuracy', color: '#33daff', icon: 'accuracy' },
  { id: 'tech', name: 'Tech', color: '#eb00ff', icon: 'tech' },
  { id: 'speed', name: 'Speed', color: '#ff6d1c', icon: 'speed' },
  { id: 'extreme', name: 'Extreme', color: '#ff3333', icon: 'extreme' }
]);
const mapCardExportSize = Object.freeze({ width: 500, height: 150 });
const mapCardGridLayout = Object.freeze({ columns: 3, columnGap: 32, rowGap: 30 });
const mapCardMetricIcons = Object.freeze({
  nps: {
    width: 24,
    height: 24,
    paths: ['M22 12h-4l-3 9L9 3l-3 9H2']
  },
  length: {
    width: 24,
    height: 24,
    paths: ['M12 6v6l4 2', 'M21 12a9 9 0 1 1-18 0 9 9 0 0 1 18 0Z']
  },
  bpm: {
    width: 24,
    height: 24,
    paths: ['M12 3 5 21h14L12 3Z', 'M12 9v5', 'M9 21h6']
  },
  difficulty: {
    width: 24,
    height: 24,
    paths: ['M18 20V10', 'M12 20V4', 'M6 20v-6']
  }
});

function formatInstanceName(index) {
  const numericIndex = Number(index);
  return Number.isFinite(numericIndex) ? `Instance ${numericIndex + 1}` : 'Instance';
}

function formatInstanceDisplayName(instance, fallbackIndex = null) {
  const numericIndex = Number(instance?.index ?? fallbackIndex);
  const fallback = formatInstanceName(numericIndex);
  const name = String(instance?.name || '').trim();
  if (!name) return fallback;

  const displayIndex = Number.isFinite(numericIndex) ? numericIndex + 1 : null;
  if (displayIndex != null) {
    const legacyNames = [
      `I-${displayIndex}`,
      `BSARR I-${displayIndex}`,
      `${managedInstanceNamePrefix}${displayIndex}`,
      `BSARR ${managedInstanceNamePrefix}${displayIndex}`
    ].map(value => value.toLowerCase());

    if (legacyNames.includes(name.toLowerCase())) {
      return fallback;
    }
  }

  return name;
}

const launchPresets = {
  '5k-monitor-2x2': {
    instanceCount: 3,
    maxConcurrentRecordings: 3,
    targetFps: 60,
    captureWidth: 2560,
    captureHeight: 1440,
    videoBitrateKbps: 18000,
    outputFormat: 'mkv',
    encoder: 'h264_nvenc',
    qualityMode: 'Performance',
    beatSaberLaunchArguments: windowed5kLaunchArguments,
    manageDisplayScale: true,
    recordingDisplayScalePercent: 100,
    restoreDisplayScalePercent: 150,
    hideTaskbarDuringRun: true
  },
  '720p-monitor-2x2': {
    instanceCount: 4,
    maxConcurrentRecordings: 4,
    targetFps: 60,
    captureWidth: 1280,
    captureHeight: 720,
    videoBitrateKbps: 8000,
    outputFormat: 'mkv',
    encoder: 'h264_nvenc',
    qualityMode: 'Performance',
    beatSaberLaunchArguments: windowed720pLaunchArguments,
    manageDisplayScale: true,
    recordingDisplayScalePercent: 100,
    restoreDisplayScalePercent: 150,
    hideTaskbarDuringRun: true
  },
  '4k-monitor-2x2': {
    instanceCount: 4,
    maxConcurrentRecordings: 4,
    targetFps: 60,
    captureWidth: 1920,
    captureHeight: 1080,
    videoBitrateKbps: 12000,
    outputFormat: 'mkv',
    encoder: 'h264_nvenc',
    qualityMode: 'Performance',
    beatSaberLaunchArguments: defaultLaunchArguments,
    manageDisplayScale: true,
    recordingDisplayScalePercent: 100,
    restoreDisplayScalePercent: 150,
    hideTaskbarDuringRun: true
  },
  '1440p-monitor-2x2': {
    instanceCount: 4,
    maxConcurrentRecordings: 4,
    targetFps: 60,
    captureWidth: 1280,
    captureHeight: 720,
    videoBitrateKbps: 8000,
    outputFormat: 'mkv',
    encoder: 'h264_nvenc',
    qualityMode: 'Performance',
    beatSaberLaunchArguments: windowed720pLaunchArguments,
    manageDisplayScale: true,
    recordingDisplayScalePercent: 100,
    restoreDisplayScalePercent: 150,
    hideTaskbarDuringRun: true
  },
  'single-720p': {
    instanceCount: 1,
    maxConcurrentRecordings: 1,
    captureWidth: 1280,
    captureHeight: 720,
    beatSaberLaunchArguments: windowed720pLaunchArguments,
    manageDisplayScale: false,
    hideTaskbarDuringRun: false
  },
  'single-1080p': {
    instanceCount: 1,
    maxConcurrentRecordings: 1,
    captureWidth: 1920,
    captureHeight: 1080,
    beatSaberLaunchArguments: defaultLaunchArguments,
    manageDisplayScale: false,
    hideTaskbarDuringRun: false
  },
  'single-1440p': {
    instanceCount: 1,
    maxConcurrentRecordings: 1,
    captureWidth: 2560,
    captureHeight: 1440,
    beatSaberLaunchArguments: windowed1440pLaunchArguments,
    manageDisplayScale: false,
    hideTaskbarDuringRun: false
  },
  'single-4k': {
    instanceCount: 1,
    maxConcurrentRecordings: 1,
    captureWidth: 3840,
    captureHeight: 2160,
    beatSaberLaunchArguments: windowed4kLaunchArguments,
    manageDisplayScale: false,
    hideTaskbarDuringRun: false
  },
  'single-5k': {
    instanceCount: 1,
    maxConcurrentRecordings: 1,
    captureWidth: 5120,
    captureHeight: 2880,
    beatSaberLaunchArguments: windowed5kLaunchArguments,
    manageDisplayScale: false,
    hideTaskbarDuringRun: false
  },
  'windowed-1080p': {
    captureWidth: 1920,
    captureHeight: 1080,
    beatSaberLaunchArguments: defaultLaunchArguments
  },
  'windowed-720p': {
    captureWidth: 1280,
    captureHeight: 720,
    beatSaberLaunchArguments: windowed720pLaunchArguments
  }
};
const launchPresetApplyDefaults = {
  'single-720p': {
    targetFps: 60,
    videoBitrateKbps: 6000,
    outputFormat: 'mkv',
    encoder: 'h264_nvenc',
    qualityMode: 'Balanced',
    recordingDisplayScalePercent: 100,
    restoreDisplayScalePercent: 150
  },
  'single-1080p': {
    targetFps: 60,
    videoBitrateKbps: 12000,
    outputFormat: 'mkv',
    encoder: 'h264_nvenc',
    qualityMode: 'Balanced',
    recordingDisplayScalePercent: 100,
    restoreDisplayScalePercent: 150
  },
  'single-1440p': {
    targetFps: 60,
    videoBitrateKbps: 18000,
    outputFormat: 'mkv',
    encoder: 'h264_nvenc',
    qualityMode: 'Balanced',
    recordingDisplayScalePercent: 100,
    restoreDisplayScalePercent: 150
  },
  'single-4k': {
    targetFps: 60,
    videoBitrateKbps: 32000,
    outputFormat: 'mkv',
    encoder: 'h264_nvenc',
    qualityMode: 'Quality',
    recordingDisplayScalePercent: 100,
    restoreDisplayScalePercent: 150
  },
  'single-5k': {
    targetFps: 60,
    videoBitrateKbps: 56000,
    outputFormat: 'mkv',
    encoder: 'h264_nvenc',
    qualityMode: 'Quality',
    recordingDisplayScalePercent: 100,
    restoreDisplayScalePercent: 150
  }
};
const setupProfiles = {
  'grid-1080p': {
    launchPreset: '4k-monitor-2x2',
    settings: {
      audioMode: 'ProcessLoopback',
      requireAudioForRun: true,
      requireAllWorkersReady: true,
      requireMatchingInstanceBaseline: true,
      audioLevelMode: 'Loudness',
      audioTargetLevelDb: -12
    }
  },
  'grid-720p': {
    launchPreset: '720p-monitor-2x2',
    settings: {
      audioMode: 'ProcessLoopback',
      requireAudioForRun: true,
      requireAllWorkersReady: true,
      requireMatchingInstanceBaseline: true,
      audioLevelMode: 'Loudness',
      audioTargetLevelDb: -12
    }
  },
  'single-1080p': {
    launchPreset: 'single-1080p',
    settings: {
      audioMode: 'ProcessLoopback',
      requireAudioForRun: true,
      requireAllWorkersReady: true,
      requireMatchingInstanceBaseline: false,
      manageDisplayScale: false,
      recordingDisplayScalePercent: 100,
      restoreDisplayScalePercent: 150,
      hideTaskbarDuringRun: false,
      audioLevelMode: 'Loudness',
      audioTargetLevelDb: -12
    }
  },
  'single-720p': {
    launchPreset: 'single-720p',
    settings: {
      audioMode: 'ProcessLoopback',
      requireAudioForRun: true,
      requireAllWorkersReady: true,
      requireMatchingInstanceBaseline: false,
      manageDisplayScale: false,
      recordingDisplayScalePercent: 100,
      restoreDisplayScalePercent: 150,
      hideTaskbarDuringRun: false,
      audioLevelMode: 'Loudness',
      audioTargetLevelDb: -12
    }
  },
  'single-1440p': {
    launchPreset: 'single-1440p',
    settings: {
      audioMode: 'ProcessLoopback',
      requireAudioForRun: true,
      requireAllWorkersReady: true,
      requireMatchingInstanceBaseline: false,
      audioLevelMode: 'Loudness',
      audioTargetLevelDb: -12
    }
  },
  'single-4k': {
    launchPreset: 'single-4k',
    settings: {
      audioMode: 'ProcessLoopback',
      requireAudioForRun: true,
      requireAllWorkersReady: true,
      requireMatchingInstanceBaseline: false,
      audioLevelMode: 'Loudness',
      audioTargetLevelDb: -12
    }
  },
  'grid-5k': {
    launchPreset: '5k-monitor-2x2',
    settings: {
      audioMode: 'ProcessLoopback',
      requireAudioForRun: true,
      requireAllWorkersReady: true,
      requireMatchingInstanceBaseline: true,
      audioLevelMode: 'Loudness',
      audioTargetLevelDb: -12
    }
  },
  'single-5k': {
    launchPreset: 'single-5k',
    settings: {
      audioMode: 'ProcessLoopback',
      requireAudioForRun: true,
      requireAllWorkersReady: true,
      requireMatchingInstanceBaseline: false,
      audioLevelMode: 'Loudness',
      audioTargetLevelDb: -12
    }
  }
};
setupProfiles['quad-4k'] = setupProfiles['grid-1080p'];
setupProfiles['quad-1440p'] = setupProfiles['grid-720p'];
setupProfiles['quad-5k'] = setupProfiles['grid-5k'];
const feedPresetDefinitions = [
  {
    profileId: 'single-720p',
    launchPreset: 'single-720p',
    title: '1x 720p stream (720p monitor)',
    detail: 'One full-resolution feed for a 1280 x 720 monitor.',
    minWidth: 1280,
    minHeight: 720,
    tier: '720p'
  },
  {
    profileId: 'single-1080p',
    launchPreset: 'single-1080p',
    title: '1x 1080p stream (4k monitor)',
    detail: 'One full-resolution feed for a 1920 x 1080 monitor.',
    minWidth: 1920,
    minHeight: 1080,
    tier: '1080p'
  },
  {
    profileId: 'single-1440p',
    launchPreset: 'single-1440p',
    title: '1x 1440p stream (1440p monitor)',
    detail: 'One full-resolution feed for a 2560 x 1440 monitor.',
    minWidth: 2560,
    minHeight: 1440,
    tier: '1440p'
  },
  {
    profileId: 'grid-720p',
    launchPreset: '720p-monitor-2x2',
    title: '4x 720p streams (1440p monitor)',
    detail: 'A 2 x 2 grid of 1280 x 720 feeds on a 1440p monitor.',
    minWidth: 2560,
    minHeight: 1440,
    tier: '1440p'
  },
  {
    profileId: 'single-4k',
    launchPreset: 'single-4k',
    title: '1x 4K stream (4k monitor)',
    detail: 'One full-resolution feed for a 3840 x 2160 monitor.',
    minWidth: 3840,
    minHeight: 2160,
    tier: '4k'
  },
  {
    profileId: 'grid-1080p',
    launchPreset: '4k-monitor-2x2',
    title: '4x 1080p streams (4k monitor)',
    detail: 'A 2 x 2 grid of 1920 x 1080 feeds on a 4K monitor.',
    minWidth: 3840,
    minHeight: 2160,
    tier: '4k'
  },
  {
    profileId: 'single-5k',
    launchPreset: 'single-5k',
    title: '1x 5K stream (5k monitor)',
    detail: 'One full-resolution feed for a 5120 x 2880 monitor.',
    minWidth: 5120,
    minHeight: 2880,
    tier: '5k'
  },
  {
    profileId: 'grid-5k',
    launchPreset: '5k-monitor-2x2',
    title: '4x 1440p streams (5k monitor)',
    detail: 'A 2 x 2 grid of 2560 x 1440 feeds on a 5K monitor.',
    minWidth: 5120,
    minHeight: 2880,
    tier: '5k'
  }
];
const launchPresetFieldIds = [
  'targetFps',
  'captureWidth',
  'captureHeight',
  'videoBitrateKbps',
  'outputFormat',
  'encoder',
  'qualityMode',
  'captureEngine',
  'beatSaberLaunchArguments',
  'manageDisplayScale',
  'recordingDisplayScalePercent',
  'hideTaskbarDuringRun'
];

function readSetupAssistantHidden() {
  try {
    return localStorage.getItem('bsarr.setupAssistantHidden') === 'true';
  } catch {
    return false;
  }
}

function writeSetupAssistantHidden(hidden) {
  setupAssistantHidden = hidden;
  try {
    if (hidden) {
      localStorage.setItem('bsarr.setupAssistantHidden', 'true');
    } else {
      localStorage.removeItem('bsarr.setupAssistantHidden');
    }
  } catch {
    // Local storage is only a convenience; the assistant still works without it.
  }
}

function readMapCardCategoriesEnabled() {
  try {
    return localStorage.getItem('bsarr.mapCardCategoriesEnabled') !== 'false';
  } catch {
    return true;
  }
}

function writeMapCardCategoriesEnabled(enabled) {
  mapCardCategoriesEnabled = enabled;
  try {
    if (enabled) {
      localStorage.removeItem('bsarr.mapCardCategoriesEnabled');
    } else {
      localStorage.setItem('bsarr.mapCardCategoriesEnabled', 'false');
    }
  } catch {
    // Local storage is only a convenience; the export toggle still works without it.
  }
}

async function loadState() {
  const response = await fetch('/api/state');
  if (!response.ok) throw new Error(await response.text());
  state = await response.json();
  render();
}

async function loadDisplayInfo() {
  const response = await fetch('/api/displays');
  if (!response.ok) throw new Error(await response.text());
  displayInfo = await response.json();
  renderMonitorOptions();
  render();
}

async function loadColorPresets() {
  const response = await fetch('/api/game-color-presets');
  if (!response.ok) throw new Error(await response.text());
  colorPresetCatalog = await response.json();
  renderColorPresetOptions();
}

async function loadSetupSourcePath() {
  const response = await fetch('/api/setup/source');
  if (!response.ok) throw new Error(await response.text());
  setupSourcePathInfo = await response.json();
  const input = document.getElementById('setupSourceBeatSaberPath');
  const detectedPath = setupSourcePathInfo.effectiveSourceBeatSaberPath || setupSourcePathInfo.detectedSourceBeatSaberPath || '';
  if (input && !input.value.trim() && detectedPath) {
    input.value = detectedPath;
  }

  renderSetupSourcePath(buildCurrentSettingsPreview());
}

function render() {
  if (!state) return;

  isRendering = true;
  try {
  const settings = state.settings;
  const enabledInstances = getDisplayedEnabledManagedInstances();
  const activeInstanceCount = getDisplayedActiveInstanceCount();
  const activeWorkers = enabledInstances.filter(instance => instance.workerId).length;
  const pendingReplays = state.queue.filter(item => isPendingStatus(item.status)).length;
  const recordingReplays = state.queue.filter(item => isActiveStatus(item.status)).length;
  const completedReplays = state.queue.filter(item => sameStatus(item.status, 'Completed')).length;
  const failedReplays = state.queue.filter(item => sameStatus(item.status, 'Failed')).length;
  const runActive = Boolean(state.run?.isRunning || state.run?.cancellationRequested);
  document.body.classList.toggle('queueLoaded', state.queue.length > 0);
  updateActiveView();
  setHidden('startRun', runActive);
  setHidden('stopRun', true);
  updateLaunchGamesButton({ runActive });
  updateCloseGamesAfterQueueButton({ runActive, pendingReplays, recordingReplays });

  document.getElementById('runPill').textContent = state.run.status;
  document.getElementById('runPill').className = `statusPill ${statusClass(state.run.status)}`;
  const runStatus = document.getElementById('runStatus');
  const runIssue = buildRunIssue();
  const runMeta = `${state.queue.length} replays | ${settings.captureWidth}x${settings.captureHeight}@${settings.targetFps} | ${formatBitrate(settings.videoBitrateKbps)} ${formatContainer(settings.outputFormat)} | ${formatMonitorSummary(settings.monitorIndex)} | ${settings.encoder} | ${formatAudioMode(settings)}`;
  runStatus.textContent = runIssue ? `${runIssue} | ${runMeta}` : runMeta;
  runStatus.classList.toggle('hasIssue', Boolean(runIssue) && hasRunIssue());
  renderCommandStatus(settings, {
    activeWorkers,
    pendingReplays,
    recordingReplays,
    completedReplays,
    failedReplays
  });
  document.getElementById('pendingCount').textContent = pendingReplays;
  document.getElementById('recordingCount').textContent = recordingReplays;
  document.getElementById('completedCount').textContent = completedReplays;
  document.getElementById('failedCount').textContent = failedReplays;
  const visibleInstanceCount = getVisibleManagedInstanceSlotCount();
  document.getElementById('workerCount').textContent = `${activeWorkers}/${activeInstanceCount}`;
  setText('pendingSub', `${state.queue.length} total`);
  setText('activeSub', `${activeWorkers} worker${activeWorkers === 1 ? '' : 's'}`);
  setText('completeSub', `${Math.round(completedReplays / Math.max(1, state.queue.length) * 100)}% done`);
  setText('failedSub', failedReplays ? 'needs review' : 'clear');
  setText('workerSub', `${activeInstanceCount} enabled, ${state.instances.length || visibleInstanceCount} configured`);
  setText('dockOutput', formatContainer(settings.outputFormat));
  setText('dockFps', settings.targetFps);
  setText('dockEncoder', settings.encoder);
  setText('dockAudio', formatAudioMode(settings));
  setText('dockBitrate', formatBitrate(settings.videoBitrateKbps));
  document.getElementById('lastUpdated').textContent = `Updated ${formatClock(new Date())}`;

  setValue('instanceCount', settings.instanceCount);
  setValue('recordingOutputDirectory', settings.recordingOutputDirectory);
  setValue('sharedCustomLevelsDirectory', settings.sharedCustomLevelsDirectory);
  setValue('sharedCustomWipLevelsDirectory', settings.sharedCustomWipLevelsDirectory);
  setValue('sharedCustomSabersDirectory', settings.sharedCustomSabersDirectory);
  setValue('sharedCustomNotesDirectory', settings.sharedCustomNotesDirectory);
  setValue('sharedCustomPlatformsDirectory', settings.sharedCustomPlatformsDirectory);
  setValue('sharedCustomAvatarsDirectory', settings.sharedCustomAvatarsDirectory);
  setValue('sharedCustomWallsDirectory', settings.sharedCustomWallsDirectory);
  setValue('sharedCustomBombsDirectory', settings.sharedCustomBombsDirectory);
  setValue('targetFps', settings.targetFps);
  setValue('captureWidth', settings.captureWidth);
  setValue('captureHeight', settings.captureHeight);
  setResolutionPreset(settings.captureWidth, settings.captureHeight);
  setValue('videoBitrateKbps', settings.videoBitrateKbps);
  setValue('outputFormat', settings.outputFormat);
  setValue('monitorIndex', settings.monitorIndex);
  renderMonitorOptions();
  setValue('encoder', settings.encoder);
  setValue('qualityMode', settings.qualityMode);
  setValue('captureEngine', settings.captureEngine || defaultCaptureEngine);
  updateCaptureEngineWarning();
  setValue('audioMode', settings.audioMode);
  setValue('audioBitrateKbps', settings.audioBitrateKbps);
  setValue('audioSampleRate', settings.audioSampleRate);
  setValue('audioLevelMode', settings.audioLevelMode || 'Loudness');
  setValue('audioTargetLevelDb', settings.audioTargetLevelDb ?? -12);
  updateAudioLevelTargetConstraints();
  setValue('beatSaberInstancesRoot', settings.beatSaberInstancesRoot);
  setValue('setupSourceBeatSaberPath', settings.sourceBeatSaberPath || setupSourcePathInfo?.effectiveSourceBeatSaberPath || '');
  setValue('beatSaberInstanceNamePrefix', settings.beatSaberInstanceNamePrefix);
  setValue('beatSaberLaunchPreset', resolveLaunchPreset(settings));
  setValue('beatSaberLaunchArguments', settings.beatSaberLaunchArguments);
  setValue('recordingDisplayScalePercent', settings.recordingDisplayScalePercent);
  setValue('restoreDisplayScalePercent', settings.restoreDisplayScalePercent);
  setText(
    'restoreDisplayScaleSummary',
    Number.isFinite(Number(settings.restoreDisplayScalePercent))
      ? `Auto-detect before recording (last ${settings.restoreDisplayScalePercent}%)`
      : 'Auto-detect before recording');
  document.getElementById('requireAllWorkersReady').checked = Boolean(settings.requireAllWorkersReady);
  document.getElementById('requireMatchingInstanceBaseline').checked = Boolean(settings.requireMatchingInstanceBaseline);
  document.getElementById('requireAudioForRun').checked = settings.requireAudioForRun !== false;
  document.getElementById('manageDisplayScale').checked = Boolean(settings.manageDisplayScale);
  document.getElementById('hideTaskbarDuringRun').checked = settings.hideTaskbarDuringRun !== false;
  document.getElementById('disableScoreSubmissions').checked = true;
  document.getElementById('disableScoreSubmissions').disabled = true;
  document.getElementById('suppressScoreSaberReplayUi').checked = true;
  document.getElementById('suppressScoreSaberReplayUi').disabled = true;
  updateDisplayScaleAvailability();
  renderGamePresentationSettings(settings);

  renderSetupAssistant();
  updateAdvancedSettingsToggle();
  renderWorkers();
  renderAttentionLog();
  renderBaseline();
  renderSyncSummary();
  renderDiagnostics();
  renderQueue();
  renderCollectionsPage();
  renderGuidedRunAssistant();
  updateSettingsDirtyBadge();
  } finally {
    isRendering = false;
  }
}

function updateLaunchGamesButton({ runActive }) {
  const button = document.getElementById('launchGames');
  if (!button) return;

  const knownGames = getKnownGameInstances();
  const workersRunning = knownGames.length > 0;
  button.classList.toggle('dangerText', workersRunning);
  button.textContent = workersRunning ? 'Stop recorders' : 'Launch';
  button.title = workersRunning
    ? 'Stop all managed Beat Saber recorders now.'
    : 'Launch all enabled managed Beat Saber workers.';
  button.disabled = !workersRunning && runActive;
}

function updateCloseGamesAfterQueueButton({ runActive, pendingReplays, recordingReplays }) {
  const button = document.getElementById('closeGamesAfterQueue');
  if (!button) return;

  const closeRequested = Boolean(state.run?.closeGamesWhenFinishedRequested);
  const hasQueue = (state.queue || []).length > 0;
  const hasUnfinishedQueue = pendingReplays > 0 || recordingReplays > 0;
  const hasKnownGames = getKnownGameInstances().length > 0;
  const shouldArmForQueue = closeRequested || runActive || hasUnfinishedQueue;

  button.classList.toggle('autoCloseArmed', closeRequested);
  button.hidden = !hasQueue && !hasKnownGames && !closeRequested;

  if (closeRequested) {
    button.textContent = 'Cancel close after queue';
    button.disabled = false;
    button.title = 'Cancel closing all managed Beat Saber games when the queue finishes.';
    return;
  }

  if (shouldArmForQueue) {
    button.textContent = 'Close after queue';
    button.disabled = !hasQueue;
    button.title = 'Close all managed Beat Saber games automatically when this queue finishes.';
    return;
  }

  button.textContent = 'Close all games';
  button.disabled = !hasKnownGames;
  button.title = hasKnownGames
    ? 'Close all managed Beat Saber games.'
    : 'No managed Beat Saber games are running.';
}

function renderCommandStatus(settings, counts) {
  setText('captureProfile', buildCaptureProfile(settings));

  const configuredInstances = getEnabledManagedInstanceCount();
  const maps = summarizeMapAvailability(state.queue || []);
  const sync = summarizeSyncCorrection(state.queue || []);
  const baselineStatus = state.instanceBaseline?.status || 'Unchecked';
  const audioRequired = settings.requireAudioForRun !== false;
  const audioActive = audioRequired && String(settings.audioMode || '').toLowerCase() === 'processloopback';

  setText('mapsChip', `${maps.ready}/${maps.total}`);
  setText('mapsChipSub', maps.total ? (maps.missing ? `${maps.missing} missing` : 'available') : 'no queue');
  setStatusDot('mapsChipDot', maps.total && maps.missing ? 'warn' : 'good');

  setText('syncChip', sync.label);
  setText('syncChipSub', sync.detail);
  setStatusDot('syncChipDot', sync.kind);

  setText('baselineChip', baselineStatus);
  setText('baselineChipSub', state.instanceBaseline?.summary || 'instance check');
  setStatusDot('baselineChipDot', sameStatus(baselineStatus, 'Matched') ? 'good' : (sameStatus(baselineStatus, 'Unchecked') ? 'warn' : 'bad'));

  setText('audioChip', audioActive ? 'Process Loopback' : 'Audio off');
  setText('audioChipSub', audioRequired ? 'required for run' : 'not required');
  setStatusDot('audioChipDot', audioActive ? 'good' : 'warn');

  const disk = state.diskSpace || {};
  const diskStatus = disk.status || 'Unchecked';
  setText('diskChip', diskStatus);
  setText('diskChipSub', disk.summary || 'recording folder');
  setStatusDot('diskChipDot', sameStatus(diskStatus, 'Ready') ? 'good' : (sameStatus(diskStatus, 'Low') || sameStatus(diskStatus, 'Unavailable') ? 'bad' : 'warn'));

  setStatusDot('workerChipDot', counts.activeWorkers >= configuredInstances ? 'good' : 'warn');
  setStatusDot('failedChipDot', counts.failedReplays ? 'bad' : 'good');
  setStatusDot('queueChipDot', (state.queue || []).length ? 'good' : 'warn');
  setStatusDot('activeChipDot', 'good');
  setStatusDot('completeChipDot', 'good');
}

function buildCaptureProfile(settings) {
  const preset = formatLaunchPresetLabel(resolveLaunchPreset(settings));
  return [
    preset,
    formatContainer(settings.outputFormat),
    formatEncoderLabel(settings.encoder),
    `${settings.targetFps || 0} FPS`,
    formatAudioMode(settings)
  ].filter(Boolean).join(' | ');
}

function formatLaunchPresetLabel(id) {
  switch (id) {
    case '5k-monitor-2x2':
      return '4x 1440p streams (5k monitor)';
    case 'single-5k':
      return '1x 5K stream (5k monitor)';
    case '4k-monitor-2x2':
      return '4x 1080p streams (4k monitor)';
    case '720p-monitor-2x2':
      return '4x 720p streams (1440p monitor)';
    case '1440p-monitor-2x2':
      return '4x 720p streams (1440p monitor)';
    case 'single-4k':
      return '1x 4K stream (4k monitor)';
    case 'single-1440p':
      return '1x 1440p stream (1440p monitor)';
    case 'single-1080p':
      return '1x 1080p stream (4k monitor)';
    case 'single-720p':
      return '1x 720p stream (720p monitor)';
    case 'windowed-1080p':
      return 'Windowed 1080p';
    case 'windowed-720p':
      return 'Windowed 720p';
    default:
      return 'Custom profile';
  }
}

function formatEncoderLabel(value) {
  const text = String(value || '').toLowerCase();
  if (text.includes('nvenc')) return text.includes('hevc') ? 'HEVC NVENC' : 'NVENC';
  if (text.includes('x264')) return 'CPU H.264';
  return value || 'Encoder';
}

function summarizeMapAvailability(queue) {
  const total = queue.length;
  let ready = 0;
  let missing = 0;
  for (const item of queue) {
    if (sameStatus(item.mapStatus, 'Found') || sameStatus(item.mapStatus, 'Downloaded')) {
      ready++;
    } else if (sameStatus(item.mapStatus, 'Missing')) {
      missing++;
    }
  }

  return { total, ready, missing };
}

function summarizeSyncCorrection(queue) {
  const completed = queue.filter(item => sameStatus(item.status, 'Completed'));
  const corrected = completed.filter(item => sameStatus(item.syncStatus, 'Corrected'));
  const offsets = queue
    .map(item => Number(item.syncCorrectionMilliseconds))
    .filter(value => Number.isFinite(value));
  const maxOffset = offsets.length ? Math.max(...offsets.map(value => Math.abs(value))) : 0;

  if (maxOffset >= 100) {
    return { kind: 'bad', label: 'Out of range', detail: `${formatSignedNumber(maxOffset.toFixed(1))} ms max` };
  }

  if (corrected.length) {
    return {
      kind: maxOffset >= 50 ? 'warn' : 'good',
      label: 'Active',
      detail: `${corrected.length}/${completed.length} corrected`
    };
  }

  return { kind: 'warn', label: 'Waiting', detail: 'marker correction' };
}

function setStatusDot(id, kind) {
  const element = document.getElementById(id);
  if (!element) return;
  const tone = kind === 'bad' ? 'bad' : (kind === 'warn' ? 'warn' : 'good');
  element.className = `statusDot ${tone}`;
  const chip = element.closest('.statusChip');
  if (!chip) return;
  chip.classList.toggle('isGood', tone === 'good');
  chip.classList.toggle('isWarn', tone === 'warn');
  chip.classList.toggle('isBad', tone === 'bad');
  chip.classList.toggle('needsAttention', tone !== 'good');
}

function renderSongFolders() {
  const report = state.songFolders || {};
  const summary = document.getElementById('songFoldersSummary');
  const list = document.getElementById('songFoldersList');
  const status = report.status || 'Unchecked';
  const checked = report.checkedAtUtc ? `Checked ${formatClock(new Date(report.checkedAtUtc))}` : 'Not checked';
  summary.innerHTML = `
    <span class="badge ${statusClass(status)}">${escapeHtml(status)}</span>
    <span class="baselineSummaryText">${escapeHtml(report.summary || 'Song folders have not been checked.')}</span>
    <span class="muted">${escapeHtml(checked)}</span>
  `;

  list.innerHTML = '';
  const links = report.links || [];
  if (!links.length) {
    const empty = document.createElement('div');
    empty.className = 'emptyState baselineEmpty';
    empty.textContent = 'No shared folder results';
    list.appendChild(empty);
    return;
  }

  for (const item of links) {
    const row = document.createElement('div');
    row.className = 'songFolderRow';
    row.innerHTML = `
      <div class="cellBlock">
        <span class="cellTitle">${escapeHtml(item.instanceName || `Instance ${Number(item.instanceIndex) + 1}`)} · ${escapeHtml(item.folderKind || 'Songs')}</span>
        <span class="cellMeta">${escapeHtml(item.instanceFolderPath || '')}</span>
      </div>
      <span class="badge ${statusClass(item.status)}">${escapeHtml(item.status)}</span>
      <div class="cellBlock">
        <span class="cellTitle">${escapeHtml(item.detail || '')}</span>
        <span class="cellMeta">${escapeHtml(item.sharedFolderPath || '')}</span>
      </div>
    `;
    list.appendChild(row);
  }
}

function renderGamePresentationSettings(settings) {
  const gamePresentation = settings.gamePresentation || {};
  const defaults = gamePresentationDefaults;

  const booleanFields = [
    'noHud',
    'loadPlayerEnvironment',
    'loadPlayerJumpDistance',
    'overrideReplayPlayerSettings',
    'ignoreModifiers',
    'applyJdFixerSettings',
    'showHead',
    'showLeftSaber',
    'showRightSaber',
    'showWatermark',
    'showTimelineMisses',
    'showTimelineBombs',
    'showTimelinePauses',
    'noTextsAndHuds',
    'advancedHud',
    'reduceDebris',
    'noFailEffects',
    'hideNoteSpawnEffect',
    'adaptiveSfx',
    'arcsHapticFeedback'
  ];
  for (const fieldId of booleanFields) {
    const element = document.getElementById(fieldId);
    if (element) element.checked = gamePresentation[fieldId] ?? defaults[fieldId];
  }
  updateAdvancedHudAvailability();

  for (const fieldId of [
    'sfxVolume',
    'saberTrailIntensity',
    'noteJumpFixedDuration',
    'noteJumpStartBeatOffset',
    'jdFixerJumpDistance',
    'jdFixerReactionTime',
    'headsetHapticIntensity'
  ]) {
    const value = Number(gamePresentation[fieldId] ?? defaults[fieldId]);
    setValue(fieldId, Number.isFinite(value) ? value : defaults[fieldId]);
    updateGameValueLabel(fieldId);
  }

  for (const fieldId of [
    'leftSaberColor',
    'rightSaberColor',
    'lightColorA',
    'lightColorB',
    'boostLightColorA',
    'boostLightColorB',
    'wallColor',
    'noteJumpDurationType',
    'jdFixerMode',
    'arcVisibility',
    'environmentEffectsFilterDefaultPreset',
    'environmentEffectsFilterExpertPlusPreset'
  ]) {
    setValue(fieldId, gamePresentation[fieldId] || defaults[fieldId]);
  }
  updateNoteJumpDurationAvailability();
  updateJdFixerAvailability();
  renderColorPresetOptions();

  const version = document.getElementById('gamePresentationVersion');
  if (version) {
    version.textContent = `v${Number(settings.gamePresentationSettingsVersion) || 1}`;
  }
}

function getAllColorPresets() {
  return [
    ...(colorPresetCatalog?.builtIn || []),
    ...(colorPresetCatalog?.beatSaber || []),
    ...(colorPresetCatalog?.saved || [])
  ];
}

function renderColorPresetOptions() {
  const select = document.getElementById('colorPresetSelect');
  if (!select) return;

  const selectedValue = select.value;
  select.innerHTML = '';

  const groups = [
    ['Built-in', colorPresetCatalog?.builtIn || []],
    ['Beat Saber', colorPresetCatalog?.beatSaber || []],
    ['Saved', colorPresetCatalog?.saved || []]
  ];

  let optionCount = 0;
  for (const [label, presets] of groups) {
    if (!presets.length) continue;
    const group = document.createElement('optgroup');
    group.label = label;
    for (const preset of presets) {
      const option = document.createElement('option');
      option.value = preset.id;
      option.textContent = preset.name;
      group.appendChild(option);
      optionCount++;
    }
    select.appendChild(group);
  }

  if (optionCount === 0) {
    const option = document.createElement('option');
    option.value = '';
    option.textContent = 'No presets found';
    select.appendChild(option);
  }

  if (selectedValue && getAllColorPresets().some(preset => preset.id === selectedValue)) {
    select.value = selectedValue;
  }

  updateColorPresetActions();
}

function updateColorPresetActions() {
  const select = document.getElementById('colorPresetSelect');
  const deleteButton = document.getElementById('deleteColorPreset');
  const selected = getAllColorPresets().find(preset => preset.id === select?.value);
  if (deleteButton) {
    deleteButton.hidden = !selected?.canDelete;
  }
}

function applyColorPreset() {
  const selectedId = document.getElementById('colorPresetSelect')?.value;
  const preset = getAllColorPresets().find(item => item.id === selectedId);
  if (!preset) {
    showToast('Choose a color preset');
    return;
  }

  for (const fieldId of colorFieldIds) {
    setValue(fieldId, preset[fieldId]);
  }

  markSettingsDirty();
  showToast(`Applied ${preset.name}`);
}

function getCurrentColorPresetSettings() {
  return Object.fromEntries(colorFieldIds.map(fieldId => [fieldId, getText(fieldId)]));
}

async function saveCurrentColorPreset() {
  const defaultName = `Preset ${(colorPresetCatalog?.saved?.length || 0) + 1}`;
  const name = window.prompt('Name this color preset', defaultName);
  if (name == null) return;

  const trimmed = name.trim();
  if (!trimmed) {
    showToast('Preset name required');
    return;
  }

  const response = await fetch('/api/game-color-presets', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      name: trimmed,
      colors: getCurrentColorPresetSettings()
    })
  });
  if (!response.ok) throw new Error(await response.text());
  colorPresetCatalog = await response.json();
  renderColorPresetOptions();
  const saved = (colorPresetCatalog.saved || []).find(preset => preset.name === trimmed);
  if (saved) document.getElementById('colorPresetSelect').value = saved.id;
  updateColorPresetActions();
  showToast(`Saved ${trimmed}`);
}

async function deleteSelectedColorPreset() {
  const selectedId = document.getElementById('colorPresetSelect')?.value;
  const preset = getAllColorPresets().find(item => item.id === selectedId);
  if (!preset?.canDelete) {
    showToast('Only saved recorder presets can be deleted');
    return;
  }

  if (!window.confirm(`Delete saved color preset "${preset.name}"?`)) {
    return;
  }

  const response = await fetch(`/api/game-color-presets/${encodeURIComponent(preset.id)}/delete`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: '{}'
  });
  if (!response.ok) throw new Error(await response.text());
  colorPresetCatalog = await response.json();
  renderColorPresetOptions();
  showToast(`Deleted ${preset.name}`);
}

function updateGameValueLabel(fieldId) {
  const input = document.getElementById(fieldId);
  const output = document.getElementById(`${fieldId}Value`);
  if (!input || !output) return;

  const value = Number(input.value);
  if (!Number.isFinite(value)) {
    output.textContent = '';
    return;
  }

  const format = input.dataset.gameValue || '';
  if (format === 'percent') {
    output.textContent = `${Math.round(value * 100)}%`;
  } else if (format === 'seconds') {
    output.textContent = `${value.toFixed(2)}s`;
  } else if (format === 'offset') {
    output.textContent = value.toFixed(1);
  } else if (format === 'distance') {
    output.textContent = value.toFixed(2);
  } else if (format === 'milliseconds') {
    output.textContent = `${Math.round(value)}ms`;
  } else {
    output.textContent = String(value);
  }
}

function updateAdvancedHudAvailability() {
  const noTextsAndHuds = document.getElementById('noTextsAndHuds');
  const advancedHud = document.getElementById('advancedHud');
  if (!noTextsAndHuds || !advancedHud) return;

  const disabled = noTextsAndHuds.checked;
  if (disabled) {
    advancedHud.checked = false;
  }

  advancedHud.disabled = disabled;
  advancedHud.closest('.gameToggle')?.classList.toggle('isDisabled', disabled);
}

function updateDisplayScaleAvailability() {
  const enabled = Boolean(document.getElementById('manageDisplayScale')?.checked);
  const fields = document.getElementById('displayScaleFields');
  fields?.classList.toggle('isDisabled', !enabled);
  document.getElementById('recordingDisplayScalePercent')?.toggleAttribute('disabled', !enabled);
}

function updateNoteJumpDurationAvailability() {
  const mode = document.getElementById('noteJumpDurationType');
  const fixedDuration = document.getElementById('noteJumpFixedDuration');
  const row = document.getElementById('noteJumpFixedDurationRow');
  if (!mode || !fixedDuration || !row) return;

  const disabled = !sameStatus(mode.value, 'Static');
  fixedDuration.disabled = disabled;
  row.classList.toggle('isDisabled', disabled);
}

function updateJdFixerAvailability() {
  const enabled = Boolean(document.getElementById('applyJdFixerSettings')?.checked);
  const mode = document.getElementById('jdFixerMode');
  const modeRow = document.getElementById('jdFixerModeRow');
  const jumpDistance = document.getElementById('jdFixerJumpDistance');
  const jumpDistanceRow = document.getElementById('jdFixerJumpDistanceRow');
  const reactionTime = document.getElementById('jdFixerReactionTime');
  const reactionTimeRow = document.getElementById('jdFixerReactionTimeRow');

  if (!mode || !jumpDistance || !reactionTime) return;

  const useJumpDistance = enabled && sameStatus(mode.value, 'JumpDistance');
  const useReactionTime = enabled && sameStatus(mode.value, 'ReactionTime');
  mode.disabled = !enabled;
  jumpDistance.disabled = !useJumpDistance;
  reactionTime.disabled = !useReactionTime;
  modeRow?.classList.toggle('isDisabled', !enabled);
  jumpDistanceRow?.classList.toggle('isDisabled', !useJumpDistance);
  reactionTimeRow?.classList.toggle('isDisabled', !useReactionTime);
}

function updateAudioLevelTargetConstraints() {
  const target = document.getElementById('audioTargetLevelDb');
  if (!target) return;

  const isLoudness = sameStatus(getText('audioLevelMode') || 'Loudness', 'Loudness');
  target.min = isLoudness ? '-70' : '-60';
  target.max = isLoudness ? '-5' : '0';
  target.step = '0.5';

  const value = Number(target.value);
  if (!Number.isFinite(value)) return;

  const min = Number(target.min);
  const max = Number(target.max);
  if (value < min) target.value = String(min);
  if (value > max) target.value = String(max);
}

function renderWorkers() {
  const instances = document.getElementById('instances');
  if (!instances) return;
  instances.innerHTML = '';

  const slots = buildManagedInstanceSlots();
  renderActiveInstanceControls(slots);
  if (!slots.length) {
    const empty = document.createElement('div');
    empty.className = 'emptyState';
    empty.textContent = 'No workers configured';
    instances.appendChild(empty);
    return;
  }

  for (const instance of slots) {
    const reservedSlot = Boolean(instance.reservedSlot);
    const enabled = isInstanceEnabledForDisplay(instance);
    const gameRunning = Boolean(instance.workerId || instance.gameProcessId);
    const assignment = enabled ? findInstanceAssignment(instance) : null;
    const heartbeat = reservedSlot ? 'Not configured' : formatHeartbeat(instance.lastHeartbeatUtc);
    const gameState = formatInstanceGameState(instance, assignment);
    const workerState = reservedSlot ? 'Reserved' : (!enabled ? 'Disabled' : (instance.workerId ? (instance.status || 'Ready') : 'Waiting'));
    const audioState = formatInstanceAudioMode(enabled && !reservedSlot);
    const assignmentTitle = assignment
      ? (assignment.songName || assignment.fileName || `Replay #${assignment.sequenceNumber}`)
      : !enabled
      ? 'Scheduling disabled'
      : 'No assignment';
    const assignmentMeta = assignment
      ? `${escapeHtml(assignment.status || 'Queued')} | ${formatSeconds(assignment.estimatedSeconds)}`
      : !enabled
      ? 'Use + to include this lane'
      : (reservedSlot ? 'Slot available when enabled' : 'Idle');
    const rowActionLabel = gameRunning ? 'Quit' : 'Launch';
    const rowActionClass = gameRunning ? 'dangerText' : '';
    const rowActionAttribute = gameRunning ? 'data-quit-index' : 'data-launch-index';
    const rowActionDisabled = reservedSlot || (!enabled && !gameRunning);
    const row = document.createElement('div');
    row.className = `workerRow instanceLane ${reservedSlot ? 'reservedSlot' : ''} ${!enabled ? 'disabledInstance' : ''} ${instance.gameLaunchError || instance.audioRoutingError ? 'hasError' : ''}`;
    row.innerHTML = `
      <div class="instanceIdentity">
        <span class="instanceDot ${enabled && (instance.workerId || instance.gameProcessId) ? 'online' : 'idle'}" aria-hidden="true"></span>
        <span class="monitorIcon" aria-hidden="true"></span>
        <div>
          <strong>${escapeHtml(formatInstanceDisplayName(instance))}</strong>
          <span>${escapeHtml(!enabled ? 'Disabled for scheduling' : (instance.recorderHostUrl || (reservedSlot ? 'Managed slot reserved' : 'Recorder host pending')))}</span>
        </div>
      </div>
      <div class="instanceMetric"><span>Heartbeat</span><strong>${escapeHtml(heartbeat)}</strong></div>
      <div class="instanceMetric"><span>Game state</span><strong>${escapeHtml(gameState)}</strong></div>
      <div class="instanceMetric"><span>Worker</span><strong>${escapeHtml(workerState)}</strong></div>
      <div class="instanceMetric"><span>Audio</span><strong>${escapeHtml(audioState)}</strong></div>
      <div class="assignmentPreview ${assignment ? '' : 'isIdle'}">
        ${assignment ? renderPlanCover(assignment) : ''}
        <div>
          <strong>${escapeHtml(assignmentTitle)}</strong>
          <span>${assignmentMeta}</span>
        </div>
      </div>
      <button class="textButton rowAction ${rowActionClass}" type="button" ${rowActionAttribute}="${instance.index}"${disabledAttr(rowActionDisabled)}>${rowActionLabel}</button>
    `;
    const launchButton = row.querySelector('[data-launch-index]');
    if (launchButton) {
      launchButton.addEventListener('click', () => launchInstance(instance.index));
    }

    const quitButton = row.querySelector('[data-quit-index]');
    if (quitButton) {
      quitButton.addEventListener('click', () => quitInstance(instance.index));
    }
    instances.appendChild(row);
  }
}

function renderActiveInstanceControls(slots = buildManagedInstanceSlots()) {
  const activeCount = getDisplayedActiveInstanceCount();
  const configuredCount = getActiveInstanceLimit(slots);
  const text = document.getElementById('activeInstanceCount');
  if (text) {
    text.textContent = `${activeCount}/${configuredCount} active`;
  }

  const decrease = document.getElementById('decreaseActiveInstances');
  const increase = document.getElementById('increaseActiveInstances');
  if (decrease) {
    decrease.disabled = activeInstanceCountActionPending || activeCount <= minManagedInstanceCount;
  }

  if (increase) {
    increase.disabled = activeInstanceCountActionPending || activeCount >= configuredCount;
  }
}

function buildManagedInstanceSlots() {
  const count = getVisibleManagedInstanceSlotCount();
  const byIndex = new Map((state.instances || []).map(instance => [Number(instance.index), instance]));
  const slots = [];
  for (let index = 0; index < count; index++) {
    slots.push(byIndex.get(index) || {
      index,
      name: formatInstanceName(index),
      reservedSlot: true
    });
  }

  return slots;
}

function getVisibleManagedInstanceSlotCount(queue = state?.queue || []) {
  return Math.min(
    visibleManagedInstanceSlots,
    Math.max(
      minManagedInstanceCount,
      visibleManagedInstanceSlots,
      state?.instances?.length || 0,
      getMaxAssignedInstanceSlot(queue)));
}

function getMaxAssignedInstanceSlot(queue) {
  return (queue || []).reduce((max, item) => {
    const assigned = Number(item.assignedInstance);
    return Number.isFinite(assigned) ? Math.max(max, assigned + 1) : max;
  }, 0);
}

function isInstanceEnabled(instance) {
  return Boolean(instance) && !instance.reservedSlot && instance.enabled !== false;
}

function isInstanceEnabledForDisplay(instance) {
  if (!instance || instance.reservedSlot) return false;
  if (pendingActiveInstanceCount == null) return isInstanceEnabled(instance);
  const index = Number(instance.index);
  return Number.isFinite(index) && index < pendingActiveInstanceCount;
}

function getEnabledManagedInstances() {
  return (state?.instances || [])
    .filter(isInstanceEnabled)
    .sort((a, b) => Number(a.index) - Number(b.index));
}

function getDisplayedEnabledManagedInstances() {
  return (state?.instances || [])
    .filter(isInstanceEnabledForDisplay)
    .sort((a, b) => Number(a.index) - Number(b.index));
}

function getActiveInstanceLimit(slots = buildManagedInstanceSlots()) {
  const configuredSlots = slots.filter(slot => !slot.reservedSlot).length;
  const settingsCount = Number(state?.settings?.instanceCount);
  const provisionCount = Number(state?.instanceProvision?.createdInstanceCount);
  return clampManagedInstanceCount(Math.max(
    minManagedInstanceCount,
    configuredSlots,
    Number.isFinite(settingsCount) ? settingsCount : 0,
    Number.isFinite(provisionCount) ? provisionCount : 0));
}

function getDisplayedActiveInstanceCount() {
  if (pendingActiveInstanceCount != null) {
    return clampManagedInstanceCount(pendingActiveInstanceCount);
  }

  return Math.max(minManagedInstanceCount, getEnabledManagedInstances().length);
}

function getKnownGameInstances() {
  return (state?.instances || []).filter(instance =>
    instance?.gameProcessId ||
    instance?.workerId ||
    sameStatus(instance?.gameLaunchStatus, 'Started') ||
    sameStatus(instance?.gameLaunchStatus, 'Already running') ||
    sameStatus(instance?.gameLaunchStatus, 'Worker online'));
}

function getEnabledManagedInstanceCount() {
  return getDisplayedActiveInstanceCount();
}

function getEnabledPlanLaneIndexes() {
  const indexes = getDisplayedEnabledManagedInstances()
    .map(instance => Number(instance.index))
    .filter(index => Number.isFinite(index));
  return indexes.length ? indexes : [0];
}

function findInstanceAssignment(instance) {
  const index = Number(instance.index);
  const queue = state.queue || [];
  if (instance.currentReplayId) {
    const current = queue.find(item => item.id === instance.currentReplayId);
    if (current) return current;
  }

  return queue.find(item => Number(item.assignedInstance) === index && isActiveStatus(item.status)) ||
    queue.find((item, itemIndex) =>
      sameStatus(item.status, 'Queued') &&
      resolvePlanLaneIndex(item, itemIndex, getVisibleManagedInstanceSlotCount(queue)) === index) ||
    queue.find(item => Number(item.assignedInstance) === index) ||
    null;
}

function formatInstanceGameState(instance, assignment) {
  if (instance.reservedSlot) return assignment ? 'Awaiting instance' : 'Available';
  if (instance.gameLaunchError) return 'Launch failed';
  if (assignment && isActiveStatus(assignment.status)) return 'Recording';
  if (instance.gameProcessId) return 'In Menu';
  return sameStatus(instance.gameLaunchStatus, 'Idle') || !instance.gameLaunchStatus
    ? 'Off'
    : instance.gameLaunchStatus;
}

function renderBaseline() {
  const report = state.instanceBaseline || {};
  const summary = document.getElementById('baselineSummary');
  const instances = document.getElementById('baselineInstances');
  const status = report.status || 'Unchecked';
  const checked = report.checkedAtUtc ? `Checked ${formatClock(new Date(report.checkedAtUtc))}` : 'Not checked';
  summary.innerHTML = `
    <span class="badge ${statusClass(status)}">${escapeHtml(status)}</span>
    <span class="baselineSummaryText">${escapeHtml(report.summary || 'Baseline has not been checked.')}</span>
    <span class="muted">${escapeHtml(checked)}</span>
  `;
  instances.innerHTML = '';

  const rows = report.instances || [];
  if (!rows.length) {
    const empty = document.createElement('div');
    empty.className = 'emptyState baselineEmpty';
    empty.textContent = 'No baseline results';
    instances.appendChild(empty);
    return;
  }

  for (const item of rows) {
    const issueCount = (item.issues || []).length;
    const issueText = issueCount
      ? item.issues.slice(0, 3).join(' ')
      : `${item.checkedFileCount || 0} files checked`;
    const suffix = issueCount > 3 ? ` +${issueCount - 3} more` : '';
    const row = document.createElement('div');
    row.className = 'baselineRow';
    row.innerHTML = `
      <div class="cellBlock">
        <span class="cellTitle">${escapeHtml(item.name || `Instance ${Number(item.index) + 1}`)}</span>
        <span class="cellMeta">${escapeHtml(item.directory || 'No instance folder')}</span>
      </div>
      <span class="badge ${statusClass(item.status)}">${escapeHtml(item.status)}</span>
      <span class="baselineIssue">${escapeHtml(issueText + suffix)}</span>
    `;
    instances.appendChild(row);
  }
}

function renderSyncSummary() {
  const summary = document.getElementById('syncSummary');
  if (!summary) return;

  const completed = (state.queue || []).filter(item => sameStatus(item.status, 'Completed'));
  const corrected = completed.filter(item => sameStatus(item.syncStatus, 'Corrected'));
  const failed = (state.queue || []).filter(item => item.error && /sync|marker/i.test(item.error));
  const latest = corrected
    .slice()
    .sort((left, right) => String(right.completedAtUtc || '').localeCompare(String(left.completedAtUtc || '')))[0];
  const status = failed.length ? 'Failed' : (completed.length ? 'Ready' : 'Waiting');
  const detail = latest
    ? `Last correction ${formatSignedNumber(Number(latest.syncCorrectionMilliseconds || 0).toFixed(1))} ms, trim ${formatNumber(latest.trimStartSeconds)}s`
    : 'Completed replays will show marker correction metadata here.';

  summary.innerHTML = `
    <div class="syncHeader">
      <span class="badge ${statusClass(status)}">${escapeHtml(status)}</span>
      <span class="cellTitle">${escapeHtml(detail)}</span>
      <span class="muted">${corrected.length}/${completed.length} corrected</span>
    </div>
    ${failed.length ? `<div class="errorText">${escapeHtml(`${failed.length} replay${failed.length === 1 ? '' : 's'} failed sync detection`)}</div>` : ''}
  `;
}

function renderDiagnostics() {
  if (!state) return;

  const settings = buildCurrentSettingsPreview();
  renderDiagnosticReadiness(settings);
  renderBenchmark();
  renderDiagnosticWorkers();
  renderDiagnosticEvents();
}

function renderDiagnosticReadiness(settings) {
  const summary = document.getElementById('diagnosticsReadinessSummary');
  const list = document.getElementById('diagnosticsReadinessList');
  if (!summary || !list) return;

  const items = buildDiagnosticsChecklist(settings);
  const failed = items.filter(item => diagnosticSeverity(item) === 'bad');
  const waiting = items.filter(item => diagnosticSeverity(item) === 'warn');
  const readyCount = items.length - failed.length - waiting.length;
  const overallStatus = failed.length ? 'Blocked' : (waiting.length ? 'Needs check' : 'Ready');
  const overallDetail = failed.length
    ? `${failed.length} blocker${failed.length === 1 ? '' : 's'} before a clean run`
    : (waiting.length ? `${waiting.length} check${waiting.length === 1 ? '' : 's'} still waiting` : 'All live checks are ready');

  summary.innerHTML = `
    <div class="diagnosticHeroState ${diagnosticSeverity({ status: overallStatus })}">
      <span class="badge ${statusClass(overallStatus)}">${escapeHtml(overallStatus)}</span>
      <div>
        <strong>${escapeHtml(overallDetail)}</strong>
        <span>${readyCount}/${items.length} checks ready</span>
      </div>
    </div>
    <div class="diagnosticHeroMeta">
      <span>${escapeHtml(formatLaunchPresetLabel(resolveLaunchPreset(settings)))}</span>
      <span>${escapeHtml(`${settings.captureWidth || 0}x${settings.captureHeight || 0}@${settings.targetFps || 0}`)}</span>
      <span>${escapeHtml(formatAudioMode(settings))}</span>
    </div>
  `;

  list.innerHTML = items.map(item => `
    <div class="diagnosticCheckItem ${diagnosticSeverity(item)}">
      <span class="diagnosticStateDot" aria-hidden="true"></span>
      <span class="badge ${statusClass(item.status)}">${escapeHtml(item.status)}</span>
      <div class="cellBlock">
        <span class="cellTitle">${escapeHtml(item.label)}</span>
        <span class="cellMeta">${escapeHtml(item.detail)}</span>
      </div>
    </div>
  `).join('');
}

function buildDiagnosticsChecklist(settings) {
  const queue = state.queue || [];
  const failedReplays = queue.filter(item => sameStatus(item.status, 'Failed') || item.error);
  const maps = summarizeMapAvailability(queue);
  const sync = summarizeSyncCorrection(queue);
  const disk = state.diskSpace || {};
  const run = state.run || {};
  const runStatus = run.status || 'Idle';
  const runDetail = run.cancellationReason
    || `${queue.length} replay${queue.length === 1 ? '' : 's'}, ${failedReplays.length} failed`;

  return [
    {
      label: 'Run state',
      status: runStatus,
      detail: runDetail
    },
    ...buildSetupWizardChecklist(settings),
    {
      label: 'Queue maps',
      status: !maps.total ? 'Waiting' : (maps.missing ? 'Missing' : (maps.ready === maps.total ? 'Ready' : 'Check')),
      detail: maps.total
        ? `${maps.ready}/${maps.total} available${maps.missing ? `, ${maps.missing} missing` : ''}`
        : 'Import replays to check map availability.'
    },
    {
      label: 'Recording disk',
      status: disk.status || 'Unchecked',
      detail: disk.summary || 'Disk space has not been checked.'
    },
    {
      label: 'Sync correction',
      status: sync.kind === 'bad' ? 'Failed' : (sync.kind === 'good' ? 'Ready' : 'Waiting'),
      detail: `${sync.label}: ${sync.detail}`
    }
  ];
}

function renderDiagnosticRuntime(settings) {
  const grid = document.getElementById('diagnosticsRuntimeGrid');
  if (!grid) return;

  const queue = state.queue || [];
  const instances = state.instances || [];
  const enabled = getEnabledManagedInstances();
  const enabledCount = enabled.length || getEnabledManagedInstanceCount();
  const activeWorkers = enabled.filter(instance => instance.workerId).length;
  const processCount = enabled.filter(instance => instance.gameProcessId).length;
  const hostCount = enabled.filter(instance => instance.recorderHostUrl).length;
  const maps = summarizeMapAvailability(queue);
  const disk = state.diskSpace || {};
  const run = state.run || {};
  const completed = queue.filter(item => sameStatus(item.status, 'Completed')).length;
  const failed = queue.filter(item => sameStatus(item.status, 'Failed') || item.error).length;
  const pending = queue.filter(item => isPendingStatus(item.status)).length;

  const rows = [
    {
      label: 'Control API',
      value: 'Online',
      status: 'Ready',
      detail: 'Polling /api/state'
    },
    {
      label: 'Run',
      value: run.status || 'Idle',
      status: run.status || 'Idle',
      detail: run.startedAtUtc ? `Started ${formatEventTime(run.startedAtUtc)}` : 'No active run'
    },
    {
      label: 'Queue',
      value: `${queue.length} replay${queue.length === 1 ? '' : 's'}`,
      status: failed ? 'Failed' : (queue.length ? 'Ready' : 'Waiting'),
      detail: `${pending} pending, ${completed} complete, ${failed} failed`
    },
    {
      label: 'Workers',
      value: `${activeWorkers}/${enabledCount} online`,
      status: activeWorkers >= enabledCount ? 'Ready' : 'Waiting',
      detail: `${processCount}/${enabledCount} Beat Saber process ids known`
    },
    {
      label: 'Recorder hosts',
      value: `${hostCount}/${enabledCount} linked`,
      status: hostCount >= enabledCount ? 'Ready' : 'Waiting',
      detail: instances.length ? `${instances.length} configured instance${instances.length === 1 ? '' : 's'}` : 'No instances reported'
    },
    {
      label: 'Output disk',
      value: disk.status || 'Unchecked',
      status: disk.status || 'Unchecked',
      detail: disk.summary || shortPath(disk.path || settings.recordingOutputDirectory || '')
    },
    {
      label: 'Capture',
      value: `${settings.captureWidth || 0}x${settings.captureHeight || 0}`,
      status: 'Ready',
      detail: `${settings.targetFps || 0} FPS, ${formatBitrate(settings.videoBitrateKbps)}, ${formatEncoderLabel(settings.encoder)}`
    },
    {
      label: 'Maps',
      value: maps.total ? `${maps.ready}/${maps.total}` : 'No queue',
      status: !maps.total ? 'Waiting' : (maps.missing ? 'Missing' : 'Ready'),
      detail: maps.missing ? `${maps.missing} missing maps` : 'Map availability clear'
    }
  ];

  grid.innerHTML = rows.map(row => `
    <div class="diagnosticMetricTile ${diagnosticSeverity({ status: row.status })}">
      <span>${escapeHtml(row.label)}</span>
      <strong>${escapeHtml(row.value)}</strong>
      <small>${escapeHtml(row.detail)}</small>
    </div>
  `).join('');
}

function renderBenchmark() {
  const summary = document.getElementById('benchmarkSummary');
  const selector = document.getElementById('benchmarkPassSelector');
  const list = document.getElementById('benchmarkPassList');
  const start = document.getElementById('startBenchmark');
  const stop = document.getElementById('stopBenchmark');
  if (!summary || !list) return;

  const benchmark = state.benchmark || {};
  const settings = benchmark.settingsSnapshot || {};
  const active = Boolean(benchmark.isRunning || benchmark.cancellationRequested);
  const runActive = Boolean(state.run?.isRunning || state.run?.cancellationRequested);
  const availableConcurrency = getBenchmarkAvailableConcurrency();
  const status = benchmark.status || 'Idle';
  const recommendedCount = benchmark.recommendedWorkerCount;
  const hasRecommendation = recommendedCount !== null &&
    recommendedCount !== undefined &&
    Number.isFinite(Number(recommendedCount));
  const recommended = hasRecommendation
    ? `${recommendedCount} worker${Number(recommendedCount) === 1 ? '' : 's'}`
    : 'No passing count yet';
  const started = benchmark.startedAtUtc ? `Started ${formatEventTime(benchmark.startedAtUtc)}` : 'Not run yet';
  const finished = benchmark.finishedAtUtc ? `Finished ${formatEventTime(benchmark.finishedAtUtc)}` : started;
  const capture = settings.captureWidth && settings.captureHeight
    ? `${settings.captureWidth}x${settings.captureHeight}@${settings.targetFps || state.settings?.targetFps || 0}`
    : `${state.settings?.captureWidth || 0}x${state.settings?.captureHeight || 0}@${state.settings?.targetFps || 0}`;
  const failure = benchmark.failureReason || '';

  if (start) {
    start.disabled = active || runActive || getSelectedBenchmarkConcurrencies().length === 0;
  }

  if (stop) {
    stop.hidden = !active;
  }

  summary.innerHTML = `
    <div class="benchmarkHeroState ${diagnosticSeverity({ status })}">
      <span class="badge ${statusClass(status)}">${escapeHtml(status)}</span>
      <div>
        <strong>${escapeHtml(active ? `Testing ${benchmark.activeConcurrency || 1}/${benchmark.maxConcurrency || 1}` : recommended)}</strong>
        <span>${escapeHtml(failure || finished)}</span>
      </div>
    </div>
    <div class="benchmarkHeroMeta">
      <span>${escapeHtml(capture)}</span>
      <span>${escapeHtml(settings.encoder || state.settings?.encoder || 'encoder')}</span>
      <span>${escapeHtml(formatAudioMode(settings.audioMode ? settings : state.settings))}</span>
      <span>${escapeHtml(benchmark.outputDirectory ? shortPath(benchmark.outputDirectory) : 'No report folder')}</span>
    </div>
  `;

  if (selector) {
    renderBenchmarkPassSelector(selector, benchmark, active, availableConcurrency);
  }

  const passes = benchmark.passes || [];
  if (!passes.length) {
    list.innerHTML = '<div class="emptyState">Run a benchmark to measure real replay capacity.</div>';
    return;
  }

  list.innerHTML = passes.map(renderBenchmarkPass).join('');
}

function renderBenchmarkPassSelector(selector, benchmark, active, availableConcurrency) {
  const maxConcurrency = Math.max(1, maxManagedInstanceCount);
  const selected = getSelectedBenchmarkConcurrencies();
  const selectedLabel = selected.length
    ? selected.join(', ')
    : 'none';
  const runningSelection = (benchmark.selectedConcurrencies || []).join(', ');
  selector.innerHTML = `
    <div class="benchmarkSelectorHeader">
      <strong>Passes</strong>
      <span>${escapeHtml(active && runningSelection ? `Running ${runningSelection}` : `Selected ${selectedLabel}`)}</span>
    </div>
    <div class="benchmarkConcurrencyOptions">
      ${Array.from({ length: maxConcurrency }, (_, index) => {
        const level = index + 1;
        const available = level <= availableConcurrency;
        const checked = selected.includes(level);
        const disabled = active || !available;
        const reason = available
          ? `${level} simultaneous recording${level === 1 ? '' : 's'}`
          : `${level} needs ${level} online worker${level === 1 ? '' : 's'}`;
        return `
          <label class="benchmarkConcurrencyOption ${checked ? 'isChecked' : ''} ${available ? '' : 'isUnavailable'}">
            <input type="checkbox" data-benchmark-concurrency="${level}" ${checked ? 'checked' : ''} ${disabled ? 'disabled' : ''}>
            <span>${level}</span>
            <small>${escapeHtml(reason)}</small>
          </label>
        `;
      }).join('')}
    </div>
  `;
}

function getBenchmarkAvailableConcurrency() {
  return Math.min(
    maxManagedInstanceCount,
    getEnabledManagedInstances()
      .filter(instance => instance.workerId && !instance.reservedSlot)
      .length);
}

function getSelectedBenchmarkConcurrencies() {
  const availableConcurrency = getBenchmarkAvailableConcurrency();
  if (!Array.isArray(selectedBenchmarkConcurrencies)) {
    selectedBenchmarkConcurrencies = Array.from(
      { length: Math.max(0, availableConcurrency) },
      (_, index) => index + 1);
  }

  selectedBenchmarkConcurrencies = selectedBenchmarkConcurrencies
    .map(level => Number(level))
    .filter(level => Number.isInteger(level) && level >= 1 && level <= availableConcurrency)
    .filter((level, index, array) => array.indexOf(level) === index)
    .sort((a, b) => a - b);
  return selectedBenchmarkConcurrencies;
}

function renderBenchmarkPass(pass) {
  const assignments = pass.assignments || [];
  const fps = [
    pass.minimumFramesPerSecond ? `min ${formatFps(pass.minimumFramesPerSecond)}` : '',
    pass.averageFramesPerSecond ? `avg ${formatFps(pass.averageFramesPerSecond)}` : ''
  ].filter(Boolean).join(' | ') || 'FPS pending';
  const detail = pass.failureReason || pass.outputSummary || `${assignments.length} assignment${assignments.length === 1 ? '' : 's'}`;

  return `
    <section class="benchmarkPass ${diagnosticSeverity({ status: pass.status })}">
      <div class="benchmarkPassHeader">
        <span class="badge ${statusClass(pass.status)}">${escapeHtml(pass.status || 'Queued')}</span>
        <strong>${escapeHtml(`${pass.concurrency || 0} worker${Number(pass.concurrency) === 1 ? '' : 's'}`)}</strong>
        <span>${escapeHtml(fps)}</span>
        <small>${escapeHtml(detail)}</small>
      </div>
      <div class="benchmarkAssignmentList">
        ${assignments.map(renderBenchmarkAssignment).join('')}
      </div>
    </section>
  `;
}

function renderBenchmarkAssignment(assignment) {
  const status = assignment.status || 'Queued';
  const fps = [
    assignment.minimumFramesPerSecond ? `min ${formatFps(assignment.minimumFramesPerSecond)}` : '',
    assignment.averageFramesPerSecond ? `avg ${formatFps(assignment.averageFramesPerSecond)}` : '',
    assignment.finalizationSeconds ? `save ${formatFinalizationSeconds(assignment.finalizationSeconds)}` : '',
    Number(assignment.heartbeatCount) ? `${assignment.heartbeatCount} hb` : ''
  ].filter(Boolean).join(' | ') || 'FPS pending';
  const output = assignment.outputPath || assignment.error || assignment.warning || assignment.syncStatus || 'Waiting for report';

  return `
    <div class="benchmarkAssignment ${diagnosticSeverity({ status })}">
      <span class="diagnosticStateDot" aria-hidden="true"></span>
      <div>
        <strong>${escapeHtml(assignment.instanceName || `Instance ${Number(assignment.instanceIndex || 0) + 1}`)}</strong>
        <span>${escapeHtml(assignment.replayLabel || assignment.sourceReplayId || 'Replay')}</span>
      </div>
      <div>
        <span>Status</span>
        <strong>${escapeHtml(status)}</strong>
      </div>
      <div>
        <span>FPS</span>
        <strong>${escapeHtml(fps)}</strong>
      </div>
      <div class="benchmarkAssignmentOutput">
        <span>Output</span>
        <strong title="${escapeHtml(output)}">${escapeHtml(shortPath(output))}</strong>
      </div>
    </div>
  `;
}

function renderDiagnosticWorkers() {
  const list = document.getElementById('diagnosticsWorkerList');
  if (!list) return;

  const slots = buildManagedInstanceSlots();
  if (!slots.length) {
    list.innerHTML = '<div class="emptyState">No workers configured</div>';
    return;
  }

  list.innerHTML = slots.map(instance => {
    const reservedSlot = Boolean(instance.reservedSlot);
    const enabled = isInstanceEnabled(instance);
    const assignment = enabled ? findInstanceAssignment(instance) : null;
    const status = reservedSlot ? 'Reserved' : (!enabled ? 'Disabled' : (instance.workerId ? (instance.status || 'Ready') : 'Waiting'));
    const audioState = reservedSlot
      ? 'Pending'
      : !enabled
      ? 'Off'
      : instance.audioRoutingError
      ? 'Failed'
      : (instance.audioRoutingStatus || (state.settings?.requireAudioForRun === false ? 'Off' : 'Waiting'));
    const assignmentTitle = assignment
      ? (assignment.songName || assignment.fileName || `Replay #${assignment.sequenceNumber}`)
      : (!enabled ? 'Scheduling disabled' : 'No assignment');
    const host = instance.recorderHostUrl || (reservedSlot ? 'Managed slot reserved' : 'Recorder host pending');
    const severity = diagnosticSeverity({ status: instance.gameLaunchError || instance.audioRoutingError ? 'Failed' : status });

    return `
      <div class="diagnosticWorkerRow ${severity} ${!enabled ? 'isDisabled' : ''}">
        <div class="diagnosticWorkerIdentity">
          <span class="diagnosticStateDot" aria-hidden="true"></span>
          <div>
            <strong>${escapeHtml(formatInstanceDisplayName(instance))}</strong>
            <span>${escapeHtml(host)}</span>
          </div>
        </div>
        <div class="diagnosticWorkerMetric"><span>Status</span><strong>${escapeHtml(status)}</strong></div>
        <div class="diagnosticWorkerMetric"><span>Heartbeat</span><strong>${escapeHtml(reservedSlot ? 'Not configured' : formatHeartbeat(instance.lastHeartbeatUtc))}</strong></div>
        <div class="diagnosticWorkerMetric"><span>Game</span><strong>${escapeHtml(formatInstanceGameState(instance, assignment))}</strong></div>
        <div class="diagnosticWorkerMetric"><span>Audio</span><strong>${escapeHtml(audioState)}</strong></div>
        <div class="diagnosticWorkerAssignment">
          <span>${escapeHtml(assignment ? (assignment.status || 'Queued') : 'Idle')}</span>
          <strong>${escapeHtml(assignmentTitle)}</strong>
        </div>
      </div>
    `;
  }).join('');
}

function renderDiagnosticEvents() {
  const log = document.getElementById('diagnosticsEventLog');
  if (!log) return;

  const events = collectAttentionEvents();
  if (!events.length) {
    log.innerHTML = '<div class="emptyState">No diagnostic events yet</div>';
    return;
  }

  log.innerHTML = events.slice(0, 12).map(event => `
    <div class="attentionItem ${event.kind}">
      <span class="attentionDot" aria-hidden="true"></span>
      <time>${escapeHtml(formatEventTime(event.time))}</time>
      <strong>${escapeHtml(event.text)}</strong>
      <span>${escapeHtml(event.tag)}</span>
    </div>
  `).join('');
}

function diagnosticSeverity(item) {
  const status = String(item?.status ?? '').toLowerCase();
  if (/blocked|fail|error|missing|mismatch|out of range|low|unavailable|wrong/.test(status)) return 'bad';
  if (/ready|matched|clean|import|off|idle|running|recording|started|online|ok|completed|linked|disabled|reserved/.test(status)) return 'good';
  return 'warn';
}

function renderAttentionLog() {
  const log = document.getElementById('attentionLog');
  if (!log) return;

  const events = collectAttentionEvents();
  setText('attentionCount', events.length);

  if (!events.length) {
    log.innerHTML = '<div class="emptyState">No attention items</div>';
    return;
  }

  log.innerHTML = events.slice(0, 4).map(event => `
    <div class="attentionItem ${event.kind}">
      <span class="attentionDot" aria-hidden="true"></span>
      <time>${escapeHtml(formatEventTime(event.time))}</time>
      <strong>${escapeHtml(event.text)}</strong>
      <span>${escapeHtml(event.tag)}</span>
    </div>
  `).join('');
}

function collectAttentionEvents() {
  let events = (state.events || []).map(event => ({
    kind: eventKindClass(event.kind),
    tag: event.tag || 'Event',
    time: event.createdAtUtc,
    text: event.text || 'Event recorded'
  }));

  if (!events.length) {
    events = buildDerivedAttentionEvents();
  }

  events.sort((left, right) => new Date(right.time || 0) - new Date(left.time || 0));
  return events;
}

function renderGuidedRunAssistant() {
  const queue = state?.queue || [];
  const pending = queue.filter(item => sameStatus(item.status, 'Queued')).length;
  const active = queue.filter(item => isActiveStatus(item.status)).length;
  const completed = queue.filter(item => sameStatus(item.status, 'Completed')).length;
  const failed = queue.filter(item => sameStatus(item.status, 'Failed') || item.error).length;
  const missingMaps = queue.filter(item => sameStatus(item.mapStatus, 'Missing'));
  const enabledInstances = getEnabledManagedInstances();
  const activeWorkers = enabledInstances.filter(instance => instance.workerId).length;
  const runActive = Boolean(state?.run?.isRunning || state?.run?.cancellationRequested);
  const readinessIssues = document.querySelectorAll('.statusStrip .statusChip.needsAttention').length;
  const diskStatus = state?.diskSpace?.status || 'Unchecked';
  const baselineStatus = state?.instanceBaseline?.status || 'Unchecked';

  setText('readinessSummary', readinessIssues ? `${readinessIssues} checks need attention` : 'Ready');

  const action = chooseGuidedRunAction({
    queue,
    pending,
    active,
    completed,
    failed,
    missingMaps,
    enabledInstances,
    activeWorkers,
    runActive,
    diskStatus,
    baselineStatus
  });

  setText('guidedActionTitle', action.title);
  setText('guidedActionText', action.text);
  const tone = document.getElementById('guidedActionTone');
  if (tone) {
    tone.textContent = action.toneLabel;
    tone.className = `assistantTone ${action.tone}`;
  }

  const buttons = document.getElementById('guidedActionButtons');
  if (!buttons) return;
  buttons.innerHTML = action.buttons.map(button => `
    <button class="${button.kind === 'primary' ? 'primaryButton' : 'secondaryButton'}" type="button" data-guided-action="${escapeHtml(button.action)}">
      ${escapeHtml(button.label)}
    </button>
  `).join('');

  buttons.querySelectorAll('[data-guided-action]').forEach(button => {
    button.addEventListener('click', () => handleGuidedRunAction(button.dataset.guidedAction));
  });
}

function chooseGuidedRunAction(context) {
  if (!context.queue.length) {
    return {
      tone: 'info',
      toneLabel: 'Start here',
      title: 'Import replays',
      text: 'Drop .bsor files, choose files, paste a replay link, or load a saved collection.',
      buttons: [
        { label: 'Import Replay Files', action: 'importFiles', kind: 'primary' },
        { label: 'Paste link', action: 'focusLink' }
      ]
    };
  }

  if (context.failed > 0) {
    return {
      tone: 'bad',
      toneLabel: `${context.failed} failed`,
      title: 'Review failed recordings',
      text: 'A replay failed during the last run. Diagnostics has the evidence and recovery actions.',
      buttons: [
        { label: 'Open diagnostics', action: 'diagnostics', kind: 'primary' },
        { label: 'Requeue failed', action: 'requeueAll' }
      ]
    };
  }

  if (context.missingMaps.length > 0) {
    return {
      tone: 'warn',
      toneLabel: `${context.missingMaps.length} maps`,
      title: 'Review map issues',
      text: 'Some queued replays need map files before they can record cleanly.',
      buttons: [
        { label: 'Review maps', action: 'reviewMaps', kind: 'primary' },
        { label: 'Diagnostics', action: 'diagnostics' }
      ]
    };
  }

  if (settingsDirty) {
    return {
      tone: 'warn',
      toneLabel: 'Unsaved',
      title: 'Save settings',
      text: 'Recording settings changed. Save them before starting the next run.',
      buttons: [
        { label: 'Save settings', action: 'saveSettings', kind: 'primary' },
        { label: 'Open settings', action: 'settings' }
      ]
    };
  }

  if (context.runActive) {
    return {
      tone: 'active',
      toneLabel: `${context.active} active`,
      title: 'Recording in progress',
      text: `${context.active} replay${context.active === 1 ? '' : 's'} recording now. The playhead tracks live queue progress.`,
      buttons: [
        { label: 'Stop run', action: 'stopRun', kind: 'primary' },
        { label: 'View diagnostics', action: 'diagnostics' }
      ]
    };
  }

  if (!context.activeWorkers) {
    return {
      tone: 'warn',
      toneLabel: 'Workers idle',
      title: 'Launch workers',
      text: 'Start the managed Beat Saber workers before recording the queue.',
      buttons: [
        { label: 'Launch workers', action: 'launchWorkers', kind: 'primary' },
        { label: 'Setup', action: 'settings' }
      ]
    };
  }

  if (context.pending > 0) {
    const issueText = context.diskStatus === 'Ready' || context.diskStatus === 'Unchecked'
      ? `${context.pending} replay${context.pending === 1 ? '' : 's'} ready for the next run.`
      : `Disk is ${context.diskStatus.toLowerCase()}; check diagnostics if this looks wrong.`;
    return {
      tone: 'good',
      toneLabel: 'Ready',
      title: 'Ready to record',
      text: issueText,
      buttons: [
        { label: 'Start run', action: 'startRun', kind: 'primary' },
        { label: 'Save collection', action: 'focusCollectionName' }
      ]
    };
  }

  if (context.completed > 0) {
    return {
      tone: 'good',
      toneLabel: 'Finished',
      title: 'Queue is recorded',
      text: 'The current queue has no pending replays. Save it, export a list, or requeue items for another pass.',
      buttons: [
        { label: 'Export list', action: 'exportQueue', kind: 'primary' },
        { label: 'Requeue all', action: 'requeueAll' }
      ]
    };
  }

  return {
    tone: 'info',
    toneLabel: 'Queue',
    title: 'Choose the next replay',
    text: 'Select a replay on the timeline to inspect or adjust it.',
    buttons: [
      { label: 'Import more', action: 'importFiles', kind: 'primary' },
      { label: 'Collections', action: 'focusCollections' }
    ]
  };
}

function handleGuidedRunAction(action) {
  switch (action) {
    case 'importFiles':
      openQueueReplayPicker();
      return;
    case 'focusLink':
      openAssistantSection('.importAssistant');
      document.getElementById('replayReferenceInput')?.focus();
      return;
    case 'focusCollections':
      activateView('collections');
      document.getElementById('collectionPageSelect')?.focus();
      return;
    case 'focusCollectionName':
      activateView('collections');
      document.getElementById('collectionPageNameInput')?.focus();
      return;
    case 'reviewMaps': {
      const missing = (state.queue || []).find(item => sameStatus(item.mapStatus, 'Missing'));
      if (missing) {
        selectedQueueId = missing.id;
        editingQueueId = null;
        renderQueue();
        document.getElementById('queueDetails')?.scrollIntoView({ block: 'nearest', behavior: 'smooth' });
      }
      return;
    }
    case 'diagnostics':
      activateView('diagnostics');
      return;
    case 'settings':
      activateView('settings');
      return;
    case 'saveSettings':
      runAction(saveSettings);
      return;
    case 'launchWorkers':
      document.getElementById('launchGames')?.click();
      return;
    case 'startRun':
      document.getElementById('startRun')?.click();
      return;
    case 'stopRun':
      document.getElementById('stopRun')?.click();
      return;
    case 'exportQueue':
      document.getElementById('exportQueue')?.click();
      return;
    case 'requeueAll':
      document.getElementById('requeueAll')?.click();
      return;
    default:
      return;
  }
}

function openAssistantSection(selector) {
  const section = document.querySelector(selector);
  if (section && 'open' in section) {
    section.open = true;
  }
}

function buildDerivedAttentionEvents() {
  const events = [];
  for (const item of state.queue || []) {
    const label = item.songName || item.fileName || `Replay #${item.sequenceNumber}`;
    if (item.error) {
      events.push({
        kind: 'bad',
        tag: 'Error',
        time: item.completedAtUtc || item.assignedAtUtc,
        text: `${label}: ${item.error}`
      });
    }
    if (sameStatus(item.mapStatus, 'Missing')) {
      events.push({
        kind: 'bad',
        tag: 'Map',
        time: item.assignedAtUtc,
        text: `Missing map: ${label}`
      });
    }
    if (isSyncOutOfRange(item)) {
      events.push({
        kind: 'bad',
        tag: 'Sync',
        time: item.completedAtUtc,
        text: `Sync out of range: ${label} ${formatPlanSync(item)}`
      });
    }
    if (isActiveStatus(item.status)) {
      events.push({
        kind: 'info',
        tag: 'Run',
        time: item.assignedAtUtc,
        text: `Recording started: ${label} on ${assignedInstanceText(item)}`
      });
    }
    if (sameStatus(item.status, 'Completed')) {
      events.push({
        kind: 'good',
        tag: 'Output',
        time: item.completedAtUtc,
        text: `Recording complete: ${label}`
      });
    }
  }

  for (const instance of state.instances || []) {
    if (instance.gameLaunchError || instance.audioRoutingError) {
      events.push({
        kind: 'bad',
        tag: 'Instance',
        time: instance.gameLaunchedAtUtc,
        text: `${formatInstanceDisplayName(instance)}: ${instance.gameLaunchError || instance.audioRoutingError}`
      });
    }
  }

  return events;
}

function eventKindClass(kind) {
  const normalized = String(kind || '').toLowerCase();
  if (normalized === 'good' || normalized === 'success') return 'good';
  if (normalized === 'bad' || normalized === 'error') return 'bad';
  if (normalized === 'warn' || normalized === 'warning') return 'bad';
  return 'info';
}

function formatEventTime(value) {
  if (!value) return '--:--';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '--:--';
  return date.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
}

function renderQueue() {
  const queue = state.queue || [];
  const visibleQueue = queue.filter(matchesQueueSearch);
  const hasSearch = Boolean(normalizedQueueSearch());
  const schedule = visibleQueue.length ? buildRunPlanSchedule(visibleQueue) : null;
  renderCollectionControls();
  setHidden('exportQueue', queue.length === 0);
  setHidden('renameUsedQueueRecordings', !queue.some(item => sameStatus(item.status, 'Completed')));
  setDisabled('renameUsedQueueRecordings', Boolean(state.run?.isRunning) || queue.some(item => isActiveStatus(item.status)));
  setHidden('requeueAll', !queue.some(isRequeueableQueueItem));
  setHidden('clearQueue', queue.length === 0);
  document.getElementById('queueSummary').textContent = queue.length
    ? `${visibleQueue.length} shown / ${queue.length} total | est ${formatRulerTime(schedule?.totalSeconds || 0)} | ${state.run.completedCount} done / ${state.run.failedCount} failed`
    : 'No replays imported';
  const rows = document.getElementById('queueRows');
  rows.innerHTML = '';
  syncQueueSelection(queue, visibleQueue);

  if (!visibleQueue.length) {
    if (queue.length) {
      const empty = document.createElement('div');
      empty.className = 'emptyState queueEmpty';
      empty.textContent = hasSearch ? 'No replays match this search' : 'No replays available';
      rows.appendChild(empty);
    } else {
      rows.appendChild(createEmptyQueueImportTarget());
    }
    renderQueueDetails();
    return;
  }

  const board = document.createElement('div');
  board.className = 'runPlanBoard';
  board.innerHTML = `
    <aside class="runPlanTimeline">
      <div class="timelineHeader">Order</div>
      <div class="timelineList">
        ${visibleQueue.map(item => renderTimelineItem(item)).join('')}
      </div>
    </aside>
    <section class="runPlanSchedule">
      ${renderTimeRuler(schedule.totalSeconds)}
      <div class="laneStack">
        ${schedule.lanes.map(lane => renderRunPlanLane(lane, visibleQueue, schedule.totalSeconds)).join('')}
      </div>
      ${renderRunPlanPlayhead(schedule.totalSeconds)}
      <div class="runPlanLegend">
        <span><i class="legendBox queued"></i>Queued</span>
        <span><i class="legendBox active"></i>Recording</span>
        <span><i class="legendBox complete"></i>Complete</span>
        <span><i class="legendBox failed"></i>Failed</span>
        <span><i class="legendDot good"></i>Map OK</span>
        <span><i class="legendDot warn"></i>Map / sync issue</span>
      </div>
    </section>
  `;

  bindRunPlanActions(board);
  bindRunPlanDrag(board);
  rows.appendChild(board);
  updateRunPlanPlayhead({ animate: false });

  renderQueueDetails();
}

function createEmptyQueueImportTarget() {
  const target = document.createElement('button');
  target.className = 'emptyState queueEmpty queueImportDrop';
  target.type = 'button';
  target.setAttribute('aria-label', 'Import replay files');
  target.innerHTML = `
    <span class="queueImportIcon" aria-hidden="true">&#8595;</span>
    <strong>Drop replays here</strong>
    <span>or click to choose replay files</span>
    <small>Imported replays will appear in the Replay Queue.</small>
  `;
  target.addEventListener('click', openQueueReplayPicker);
  bindQueueImportDropTarget(target);
  return target;
}

function renderCollectionControls() {
  const collections = state.collections || [];
  const queue = state.queue || [];
  const selects = getCollectionSelectElements();
  const selectedValue = selectedCollectionId || selects.find(select => select.value)?.value || '';
  const nextSelection = collections.some(collection => collection.id === selectedValue)
    ? selectedValue
    : (collections[0]?.id || '');
  selectedCollectionId = nextSelection || null;

  for (const select of selects) {
    renderCollectionSelect(select, collections, nextSelection);
  }

  for (const id of ['saveCollection', 'collectionPageSave']) setDisabled(id, queue.length === 0);
  for (const id of ['loadCollection', 'collectionPageLoad']) setDisabled(id, collections.length === 0);
  setDisabled('collectionPageRename', collections.length === 0);
  setDisabled('collectionPageDelete', collections.length === 0);
  for (const id of ['exportCollectionCards', 'collectionPageExportCards']) setDisabled(id, collections.length === 0);
  const selectedCollection = collections.find(collection => collection.id === selectedCollectionId) || collections[0] || null;
  const selectedCollectionItems = Array.isArray(selectedCollection?.items) ? selectedCollection.items : [];
  const selectedCollectionHasRecordings = Boolean(selectedCollection?.items?.some(item => item.completedOutputPath));
  setDisabled('collectionPageExportList', selectedCollectionItems.length === 0);
  setDisabled(
    'collectionPageRenameRecordings',
    !selectedCollectionHasRecordings || Boolean(state.run?.isRunning) || queue.some(item => isActiveStatus(item.status)));
  setDisabled('collectionPageImportFiles', collections.length === 0);
  setDisabled('collectionPageAddReplays', collections.length === 0);
  const collectionSelection = document.getElementById('collectionFileSelection');
  if (collectionSelection && !selectedCollectionReplayFiles.length) {
    collectionSelection.textContent = collections.length
      ? 'Drop .bsor or .dat files here'
      : 'Create or select a collection first';
  }

  const quickNameInput = document.getElementById('collectionNameInput');
  if (quickNameInput) {
    quickNameInput.placeholder = queue.length ? 'Collection name' : 'Import replays first';
  }
  const pageNameInput = document.getElementById('collectionPageNameInput');
  if (pageNameInput) {
    pageNameInput.placeholder = selectedCollection?.name || 'New collection name';
  }
}

function getCollectionSelectElements() {
  return Array.from(document.querySelectorAll('[data-collection-select]'));
}

function renderCollectionSelect(select, collections, selectedValue) {
  if (!select) return;
  select.innerHTML = '';
  if (!collections.length) {
    const option = document.createElement('option');
    option.value = '';
    option.textContent = 'No saved collections';
    select.appendChild(option);
    return;
  }

  for (const collection of collections) {
    const option = document.createElement('option');
    option.value = collection.id;
    const count = Array.isArray(collection.items) ? collection.items.length : 0;
    option.textContent = `${collection.name || 'Untitled collection'} (${count})`;
    select.appendChild(option);
  }

  select.value = collections.some(collection => collection.id === selectedValue)
    ? selectedValue
    : collections[0].id;
}

function getSelectedCollectionId() {
  return selectedCollectionId ||
    document.getElementById('collectionPageSelect')?.value ||
    document.getElementById('collectionSelect')?.value ||
    '';
}

function getCollectionNameInputValue() {
  const pageValue = (document.getElementById('collectionPageNameInput')?.value || '').trim();
  const quickValue = (document.getElementById('collectionNameInput')?.value || '').trim();
  return pageValue || quickValue;
}

function defaultCollectionName() {
  const now = new Date();
  const date = now.toLocaleDateString([], { month: 'short', day: 'numeric' });
  const time = now.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit' });
  return `Collection ${date} ${time}`;
}

async function saveCurrentCollection() {
  if (!(state.queue || []).length) {
    showToast('Import replays before saving a collection');
    return;
  }

  const name = getCollectionNameInputValue() || defaultCollectionName();
  const response = await fetch('/api/collections', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name })
  });
  if (!response.ok) throw new Error(await response.text());
  const result = await response.json();
  state = result.state;
  for (const id of ['collectionNameInput', 'collectionPageNameInput']) {
    const input = document.getElementById(id);
    if (input) input.value = '';
  }
  selectedCollectionId = result.collection?.id || selectedCollectionId;
  render();
  showToast(`Saved ${result.collection?.name || name}`);
}

async function createEmptyCollection() {
  const name = getCollectionNameInputValue() || defaultCollectionName();
  const response = await fetch('/api/collections', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name, createEmpty: true })
  });
  if (!response.ok) throw new Error(await response.text());
  const result = await response.json();
  state = result.state;
  for (const id of ['collectionNameInput', 'collectionPageNameInput']) {
    const input = document.getElementById(id);
    if (input) input.value = '';
  }
  selectedCollectionId = result.collection?.id || selectedCollectionId;
  clearCollectionReplaySelection();
  render();
  showToast(`Created ${result.collection?.name || name}`);
}

async function loadSelectedCollection() {
  const collectionId = getSelectedCollectionId();
  if (!collectionId) {
    showToast('Choose a collection');
    return;
  }

  const overwriteRecorded = activeView === 'collections'
    ? Boolean(document.getElementById('collectionPageOverwriteRecorded')?.checked)
    : Boolean(document.getElementById('collectionOverwriteRecorded')?.checked);
  const response = await fetch(`/api/collections/${encodeURIComponent(collectionId)}/load`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ overwriteRecorded })
  });
  if (!response.ok) throw new Error(await response.text());
  const result = await response.json();
  state = result.state;
  render();
  const skipped = Number(result.skippedRecordedCount) || 0;
  const missing = Number(result.missingCount) || 0;
  const suffix = [
    skipped ? `${skipped} skipped recorded` : '',
    missing ? `${missing} missing` : ''
  ].filter(Boolean).join(', ');
  showToast(`Loaded ${result.loadedCount || 0} replay${result.loadedCount === 1 ? '' : 's'}${suffix ? `, ${suffix}` : ''}`);
}

async function renameSelectedCollection() {
  const collectionId = getSelectedCollectionId();
  if (!collectionId) {
    showToast('Choose a collection');
    return;
  }

  const name = (document.getElementById('collectionPageNameInput')?.value || '').trim();
  if (!name) {
    showToast('Enter a collection name');
    document.getElementById('collectionPageNameInput')?.focus();
    return;
  }

  const response = await fetch(`/api/collections/${encodeURIComponent(collectionId)}/rename`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ name })
  });
  if (!response.ok) throw new Error(await response.text());
  const result = await response.json();
  state = result.state;
  selectedCollectionId = result.collection?.id || collectionId;
  const nameInput = document.getElementById('collectionPageNameInput');
  if (nameInput) nameInput.value = '';
  render();
  showToast(`Renamed ${result.collection?.name || name}`);
}

async function deleteSelectedCollection() {
  const collectionId = getSelectedCollectionId();
  if (!collectionId) {
    showToast('Choose a collection');
    return;
  }

  const collection = (state.collections || []).find(item => item.id === collectionId);
  const name = collection?.name || 'collection';
  if (!confirm(`Delete ${name}?`)) {
    return;
  }

  const response = await fetch(`/api/collections/${encodeURIComponent(collectionId)}/delete`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: '{}'
  });
  if (!response.ok) throw new Error(await response.text());
  state = await response.json();
  render();
  showToast(`Deleted ${name}`);
}

function renderCollectionsPage() {
  const list = document.getElementById('collectionsList');
  if (!list) return;

  const collections = state.collections || [];
  const selected = collections.find(collection => collection.id === selectedCollectionId) || collections[0] || null;
  selectedCollectionId = selected?.id || null;
  setText('collectionsPageSummary', collections.length
    ? `${collections.length} saved queue${collections.length === 1 ? '' : 's'}`
    : 'No saved queues yet');

  if (!collections.length) {
    list.innerHTML = `
      <button class="collectionListEmpty" type="button" data-collection-import>
        <strong>No collections yet</strong>
        <span>Create a collection, then drop replay files into it.</span>
      </button>
    `;
    list.querySelector('[data-collection-import]')?.addEventListener('click', () => {
      document.getElementById('collectionPageNameInput')?.focus();
    });
  } else {
    list.innerHTML = collections.map(collection => renderCollectionListCard(collection, selected?.id)).join('');
    list.querySelectorAll('[data-collection-id]').forEach(button => {
      button.addEventListener('click', () => {
        selectedCollectionId = button.dataset.collectionId || null;
        renderCollectionControls();
        renderCollectionsPage();
      });
    });
  }

  renderCollectionPreview(selected);
}

function renderCollectionListCard(collection, selectedId) {
  const stats = getCollectionStats(collection);
  const isSelected = collection.id === selectedId;
  return `
    <button class="collectionListCard ${isSelected ? 'selected' : ''}" type="button" data-collection-id="${escapeHtml(collection.id)}">
      <span class="collectionListTitle">${escapeHtml(collection.name || 'Untitled collection')}</span>
      <span class="collectionListMeta">${escapeHtml(formatCollectionUpdated(collection))}</span>
      <span class="collectionListStats">
        <strong>${stats.ready}</strong><small>ready</small>
        <strong>${stats.recorded}</strong><small>recorded</small>
        <strong>${stats.total}</strong><small>total</small>
      </span>
    </button>
  `;
}

function renderCollectionPreview(collection) {
  const statsContainer = document.getElementById('collectionPreviewStats');
  const timeline = document.getElementById('collectionPreviewTimeline');
  const items = document.getElementById('collectionPreviewItems');

  if (!collection) {
    setText('collectionPreviewTitle', 'No collection selected');
    setText('collectionPreviewMeta', 'Save the current queue or import replays to begin.');
    if (statsContainer) statsContainer.innerHTML = '';
    if (timeline) timeline.innerHTML = '<div class="collectionPreviewEmpty">Collections are saved queue presets.</div>';
    if (items) items.innerHTML = '';
    return;
  }

  const stats = getCollectionStats(collection);
  setText('collectionPreviewTitle', collection.name || 'Untitled collection');
  setText('collectionPreviewMeta', `${stats.total} replay${stats.total === 1 ? '' : 's'} | ${formatCollectionUpdated(collection)} | ${formatRulerTime(stats.totalSeconds)} total length`);
  if (statsContainer) {
    statsContainer.innerHTML = `
      <div><strong>${stats.ready}</strong><span>Ready</span></div>
      <div><strong>${stats.recorded}</strong><span>Recorded</span></div>
      <div><strong>${formatRulerTime(stats.totalSeconds)}</strong><span>Total length</span></div>
    `;
  }

  if (timeline) {
    const total = Math.max(1, stats.total);
    timeline.innerHTML = `
      <div class="collectionMiniTimeline" aria-label="Collection recording state">
        ${(collection.items || []).map(item => {
          const recorded = Boolean(item.completedOutputPath || item.completedAtUtc);
          return `<span class="${recorded ? 'recorded' : 'ready'}" style="--segment: ${100 / total}%"></span>`;
        }).join('')}
      </div>
      <div class="collectionTimelineLegend">
        <span><i class="legendDot good"></i>Ready to queue</span>
        <span><i class="legendDot mutedDot"></i>Already recorded</span>
      </div>
    `;
  }

  if (items) {
    const rows = collection.items || [];
    items.innerHTML = rows.length
      ? rows.map(renderCollectionPreviewItem).join('')
      : '<div class="collectionPreviewEmpty">This collection has no saved replays.</div>';
  }
}

function renderCollectionPreviewItem(item) {
  const recorded = Boolean(item.completedOutputPath || item.completedAtUtc);
  return `
    <article class="collectionPreviewItem ${recorded ? 'recorded' : 'ready'}">
      ${renderPlanCover(item)}
      <div>
        <strong>${escapeHtml(item.songName || item.fileName || 'Replay')}</strong>
        <span>${escapeHtml([formatPlayerName(item.playerName), item.difficulty, item.mapper].filter(Boolean).join(' | ') || 'Replay')}</span>
      </div>
      <time>${formatSeconds(item.estimatedSeconds)}</time>
      <span class="collectionItemState">${recorded ? 'Recorded' : 'Ready'}</span>
    </article>
  `;
}

function getCollectionStats(collection) {
  const items = Array.isArray(collection?.items) ? collection.items : [];
  const recorded = items.filter(item => item.completedOutputPath || item.completedAtUtc).length;
  const totalSeconds = items.reduce((sum, item) => sum + Math.max(0, Number(item.estimatedSeconds) || 0), 0);
  return {
    total: items.length,
    recorded,
    ready: Math.max(0, items.length - recorded),
    totalSeconds
  };
}

function formatCollectionUpdated(collection) {
  const value = collection?.updatedAtUtc || collection?.createdAtUtc;
  if (!value) return 'No date';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return 'No date';
  return `Updated ${date.toLocaleDateString([], { month: 'short', day: 'numeric' })}`;
}

function buildRunPlanSchedule(visibleQueue) {
  const configuredCount = getVisibleManagedInstanceSlotCount(visibleQueue);
  const lanes = [];
  for (let index = 0; index < configuredCount; index++) {
    lanes.push({
      index,
      instance: state.instances.find(item => Number(item.index) === index),
      items: [],
      totalSeconds: 0
    });
  }

  let queuedPlanIndex = 0;
  for (const [itemIndex, item] of visibleQueue.entries()) {
    const planIndex = sameStatus(item.status, 'Queued') ? queuedPlanIndex++ : itemIndex;
    const laneIndex = resolvePlanLaneIndex(item, planIndex, configuredCount);
    const lane = lanes[laneIndex] || lanes[0];
    const durationSeconds = getPlanDurationSeconds(item);
    const startLeadInSeconds = getPlanStartLeadInSeconds();
    const scheduled = {
      item,
      startSeconds: lane.totalSeconds,
      durationSeconds,
      startLeadInSeconds,
      endSeconds: lane.totalSeconds + durationSeconds,
      gapAfterSeconds: getPlanInterReplayGapSeconds()
    };
    lane.items.push(scheduled);
    lane.totalSeconds = scheduled.endSeconds + scheduled.gapAfterSeconds;
  }

  for (const lane of lanes) {
    const lastItem = lane.items[lane.items.length - 1];
    if (!lastItem) continue;
    lastItem.gapAfterSeconds = 0;
    lane.totalSeconds = lastItem.endSeconds;
  }

  return {
    lanes,
    totalSeconds: Math.max(1, ...lanes.map(lane => lane.totalSeconds))
  };
}

function resolvePlanLaneIndex(item, itemIndex, laneCount) {
  if (sameStatus(item.status, 'Queued')) {
    const enabledIndexes = getEnabledPlanLaneIndexes().filter(index => index >= 0 && index < laneCount);
    const assigned = Number(item.assignedInstance);
    if (Number.isFinite(assigned) && enabledIndexes.includes(assigned)) return assigned;

    return enabledIndexes[itemIndex % Math.max(1, enabledIndexes.length)] ?? 0;
  }

  const assigned = Number(item.assignedInstance);
  if (Number.isFinite(assigned) && assigned >= 0 && assigned < laneCount) return assigned;

  return itemIndex % Math.max(1, laneCount);
}

function getPlanDurationSeconds(item) {
  return getPlanStartLeadInSeconds() + getPlanPlaybackSeconds(item) + getPlanFixedOverheadSeconds();
}

function getPlanPlaybackSeconds(item) {
  const seconds = Number(item?.estimatedSeconds);
  return Number.isFinite(seconds) && seconds > 0 ? seconds : 60;
}

function getPlanFixedOverheadSeconds() {
  return runPlanTimingDefaults.recorderStartupSeconds +
    runPlanTimingDefaults.syncMarkerSeconds +
    runPlanTimingDefaults.recorderFinalizationSeconds;
}

function getPlanStartLeadInSeconds() {
  return runPlanTimingDefaults.startLeadInSeconds;
}

function getPlanInterReplayGapSeconds() {
  return getConfiguredInterReplayGapSeconds();
}

function getConfiguredInterReplayGapSeconds() {
  const configured = Number(state?.settings?.delayBetweenRecordingsSeconds);
  const seconds = Number.isFinite(configured)
    ? configured
    : runPlanTimingDefaults.interReplayGapSeconds;
  return Math.max(0, Math.min(30, seconds));
}

function renderTimeRuler(totalSeconds) {
  return `
    <div class="timeRuler" aria-label="Estimated queue wall-clock time" data-run-plan-total-seconds="${Number(totalSeconds).toFixed(3)}">
      <div class="timeRulerTrack" data-run-plan-ruler-track>
        ${buildTimeRulerTicks(totalSeconds).map(tick => `
          <span style="left: ${formatPercent(tick.position)}">${escapeHtml(tick.label)}</span>
        `).join('')}
      </div>
    </div>
  `;
}

function renderRunPlanPlayhead(totalSeconds) {
  const visible = shouldShowRunPlanPlayhead();
  const initialLeft = visible && Number.isFinite(lastRunPlanPlayheadLeftPx)
    ? Math.max(0, Math.round(lastRunPlanPlayheadLeftPx))
    : 0;
  return `
    <div class="runPlanPlayhead instant ${visible ? '' : 'idle'}" data-run-plan-playhead data-total-seconds="${Number(totalSeconds).toFixed(3)}" aria-hidden="true" style="left: ${initialLeft}px">
      <span class="playheadTime" data-playhead-time>0:00</span>
      <span class="playheadLine"></span>
    </div>
  `;
}

function shouldShowRunPlanPlayhead() {
  if (!state) return false;
  if (state.run?.isRunning || state.run?.cancellationRequested) return true;
  return (state.queue || []).some(item => isActiveStatus(item.status));
}

function updateRunPlanPlayhead(options = {}) {
  const playhead = document.querySelector('[data-run-plan-playhead]');
  const track = document.querySelector('[data-run-plan-card-track]') || document.querySelector('[data-run-plan-ruler-track]');
  const schedule = document.querySelector('.runPlanSchedule');
  if (!playhead || !track || !schedule || !state) return;

  const animate = options.animate !== false;
  const totalSeconds = Math.max(1, Number(playhead.dataset.totalSeconds) || Number(document.querySelector('.timeRuler')?.dataset.runPlanTotalSeconds) || 1);
  const runActive = Boolean(state.run?.isRunning || state.run?.cancellationRequested);
  const visible = shouldShowRunPlanPlayhead();
  const timelineSeconds = visible ? getRunPlayheadTimelineSeconds(totalSeconds) : 0;
  const position = Math.max(0, Math.min(1, timelineSeconds / totalSeconds));
  const scheduleRect = schedule.getBoundingClientRect();
  const trackRect = track.getBoundingClientRect();
  const left = (trackRect.left - scheduleRect.left) + trackRect.width * position;
  const timeLabel = playhead.querySelector('[data-playhead-time]');

  playhead.classList.toggle('instant', !animate);
  playhead.classList.toggle('idle', !visible);
  playhead.classList.toggle('running', runActive);
  playhead.classList.toggle('finished', visible && !runActive && state.run?.finishedAtUtc);
  if (timeLabel) {
    timeLabel.textContent = formatTimelineElapsed(timelineSeconds);
    positionPlayheadTimeLabel(timeLabel, left, trackRect, scheduleRect);
  }
  const roundedLeft = Math.round(left);
  playhead.style.left = `${roundedLeft}px`;
  lastRunPlanPlayheadLeftPx = roundedLeft;
  if (!animate) {
    if (runPlanPlayheadInstantTimeout) {
      clearTimeout(runPlanPlayheadInstantTimeout);
    }
    runPlanPlayheadInstantTimeout = setTimeout(() => {
      if (playhead.isConnected) {
        playhead.classList.remove('instant');
      }
      runPlanPlayheadInstantTimeout = null;
    }, 420);
  }
}

function positionPlayheadTimeLabel(timeLabel, lineLeft, trackRect, scheduleRect) {
  const labelWidth = Math.max(44, timeLabel.getBoundingClientRect().width || timeLabel.offsetWidth || 44);
  const trackStart = Math.max(0, trackRect.left - scheduleRect.left);
  const trackEnd = Math.min(scheduleRect.width, trackRect.right - scheduleRect.left);
  const minLabelLeft = trackStart;
  const maxLabelLeft = Math.max(minLabelLeft, trackEnd - labelWidth);
  const centeredLabelLeft = lineLeft - (labelWidth / 2);
  const labelLeft = Math.max(minLabelLeft, Math.min(maxLabelLeft, centeredLabelLeft));
  const pointerLeft = Math.max(7, Math.min(labelWidth - 7, lineLeft - labelLeft));

  timeLabel.style.setProperty('--playhead-time-offset', `${Math.round(labelLeft - lineLeft)}px`);
  timeLabel.style.setProperty('--playhead-time-pointer', `${Math.round(pointerLeft)}px`);
}

function getRunPlayheadTimelineSeconds(totalSeconds) {
  const runActive = Boolean(state.run?.isRunning || state.run?.cancellationRequested);
  if (runActive) {
    const runElapsedSeconds = getRunElapsedTimelineSeconds(Date.now());
    if (Number.isFinite(runElapsedSeconds)) {
      return Math.max(0, runElapsedSeconds);
    }
  }

  if (state.run?.finishedAtUtc) {
    const runElapsedSeconds = getRunElapsedTimelineSeconds(state.run.finishedAtUtc);
    if (Number.isFinite(runElapsedSeconds)) {
      return Math.max(0, runElapsedSeconds);
    }
  }

  const cards = Array.from(document.querySelectorAll('.runPlanCard'));
  const activePositions = cards
    .map(card => getActiveCardTimelineSeconds(card))
    .filter(value => Number.isFinite(value));

  if (activePositions.length) {
    return Math.max(0, Math.min(totalSeconds, Math.min(...activePositions)));
  }

  const completedPositions = cards
    .map(card => getCompletedCardEndSeconds(card))
    .filter(value => Number.isFinite(value));
  if ((state.run?.isRunning || state.run?.cancellationRequested || state.run?.finishedAtUtc) && completedPositions.length) {
    return Math.max(0, Math.min(totalSeconds, Math.max(...completedPositions)));
  }

  return 0;
}

function getRunElapsedTimelineSeconds(endValue) {
  const startedAt = parseTimelineDate(state.run?.startedAtUtc);
  const endedAt = parseTimelineDate(endValue);
  if (!startedAt || !endedAt) return NaN;
  return Math.max(0, (endedAt.getTime() - startedAt.getTime()) / 1000);
}

function parseTimelineDate(value) {
  if (!value) return null;
  const date = value instanceof Date ? value : new Date(value);
  return Number.isNaN(date.getTime()) ? null : date;
}

function getActiveCardTimelineSeconds(card) {
  const item = findQueueItemByCard(card);
  if (!item || !isActiveStatus(item.status)) return NaN;

  const startSeconds = Number(card.dataset.startSeconds);
  const durationSeconds = Number(card.dataset.durationSeconds);
  if (!Number.isFinite(startSeconds) || !Number.isFinite(durationSeconds)) return NaN;
  const startLeadInSeconds = Math.max(
    0,
    Math.min(durationSeconds, Number(card.dataset.startLeadInSeconds) || 0));
  const movableDurationSeconds = Math.max(0.001, durationSeconds - startLeadInSeconds);

  const startedAt = item.assignedAtUtc ? new Date(item.assignedAtUtc) : null;
  if (!startedAt || Number.isNaN(startedAt.getTime())) return startSeconds;

  const activeSeconds = Math.max(0, (Date.now() - startedAt.getTime()) / 1000);
  if (activeSeconds <= startLeadInSeconds) return startSeconds;

  const movementSeconds = activeSeconds - startLeadInSeconds;
  const progressSeconds = Math.min(
    durationSeconds,
    movementSeconds * (durationSeconds / movableDurationSeconds));
  return startSeconds + progressSeconds;
}

function getCompletedCardEndSeconds(card) {
  const item = findQueueItemByCard(card);
  if (!item || (!sameStatus(item.status, 'Completed') && !sameStatus(item.status, 'Failed'))) return NaN;

  const startSeconds = Number(card.dataset.startSeconds);
  const durationSeconds = Number(card.dataset.durationSeconds);
  if (!Number.isFinite(startSeconds) || !Number.isFinite(durationSeconds)) return NaN;
  const endSeconds = startSeconds + durationSeconds;
  const gapAfterSeconds = Math.max(0, Number(card.dataset.gapAfterSeconds) || 0);
  if (!gapAfterSeconds || !state.run?.isRunning) return endSeconds;

  const completedAt = item.completedAtUtc ? new Date(item.completedAtUtc) : null;
  if (!completedAt || Number.isNaN(completedAt.getTime())) return endSeconds;

  const gapSeconds = Math.max(0, (Date.now() - completedAt.getTime()) / 1000);
  return endSeconds + Math.min(gapAfterSeconds, gapSeconds);
}

function findQueueItemByCard(card) {
  const id = card?.dataset?.selectId;
  if (!id) return null;
  return (state.queue || []).find(item => item.id === id) || null;
}

function buildTimeRulerTicks(totalSeconds) {
  const safeTotal = Math.max(1, Number(totalSeconds) || 1);
  const tickCount = 5;
  const ticks = [{ position: 0, label: '0:00' }];
  for (let index = 1; index < tickCount; index++) {
    const seconds = safeTotal * (index / (tickCount - 1));
    ticks.push({
      position: index / (tickCount - 1),
      label: formatRulerTime(seconds)
    });
  }

  return ticks;
}

function formatRulerTime(seconds) {
  const rounded = Math.max(1, Math.round(Number(seconds) || 0));
  const hours = Math.floor(rounded / 3600);
  const minutes = Math.floor((rounded % 3600) / 60);
  const remainingSeconds = rounded % 60;
  if (hours > 0) return `${hours}:${String(minutes).padStart(2, '0')}:${String(remainingSeconds).padStart(2, '0')}`;
  return `${minutes}:${String(remainingSeconds).padStart(2, '0')}`;
}

function formatTimelineElapsed(seconds) {
  const rounded = Math.max(0, Math.round(Number(seconds) || 0));
  const hours = Math.floor(rounded / 3600);
  const minutes = Math.floor((rounded % 3600) / 60);
  const remainingSeconds = rounded % 60;
  if (hours > 0) return `${hours}:${String(minutes).padStart(2, '0')}:${String(remainingSeconds).padStart(2, '0')}`;
  return `${minutes}:${String(remainingSeconds).padStart(2, '0')}`;
}

function formatPercent(value) {
  const percent = Math.max(0, Math.min(1, Number(value) || 0)) * 100;
  return `${percent.toFixed(3)}%`;
}

function formatRunPlanLaneSubtitle(instance, enabled) {
  if (!enabled) return 'Disabled';

  let status = 'Offline';
  if (instance?.workerId) {
    status = `${instance.status || 'Ready'} worker`;
  } else if (hasLiveInstanceSignal(instance)) {
    status = instance?.gameLaunchStatus || 'Waiting for worker';
  }

  const fps = instance?.workerId ? formatHeartbeatFps(instance) : '';
  return fps ? `${status} | ${fps}` : status;
}

function formatHeartbeatFps(instance) {
  const fps = Number(instance?.lastReportedFramesPerSecond);
  return Number.isFinite(fps) && fps > 0 ? formatFps(fps) : '';
}

function hasLiveInstanceSignal(instance) {
  return Boolean(instance?.workerId || instance?.gameProcessId);
}

function isRunPlanWorkerBusy(instance) {
  return Boolean(instance?.workerId && isActiveStatus(instance.status));
}

function areRunPlanWorkerControlsLocked() {
  return runPlanWorkerActionPending || Date.now() < runPlanWorkerActionLockedUntil;
}

function lockRunPlanWorkerControls(milliseconds) {
  runPlanWorkerActionLockedUntil = Date.now() + Math.max(0, Number(milliseconds) || 0);
  if (runPlanWorkerActionUnlockTimeout) {
    clearTimeout(runPlanWorkerActionUnlockTimeout);
    runPlanWorkerActionUnlockTimeout = null;
  }

  runPlanWorkerActionUnlockTimeout = setTimeout(() => {
    runPlanWorkerActionUnlockTimeout = null;
    if (!runPlanWorkerActionPending && Date.now() >= runPlanWorkerActionLockedUntil) {
      renderQueue();
    }
  }, Math.max(0, Number(milliseconds) || 0) + 25);
}

function renderRunPlanWorkerControls(instance, index) {
  const activeCount = getDisplayedActiveInstanceCount();
  const limit = getActiveInstanceLimit();
  const controlLaneIndex = Math.max(0, Math.min(activeCount, limit) - 1);
  if (Number(index) !== controlLaneIndex) return '';
  if (activeCount <= minManagedInstanceCount && activeCount >= limit) return '';

  const highestActiveInstance = (state.instances || [])
    .find(item => Number(item.index) === activeCount - 1);
  const name = formatInstanceDisplayName(instance, index);
  const controlsLocked = activeInstanceCountActionPending || areRunPlanWorkerControlsLocked();
  const canEnable = activeCount < limit && !controlsLocked;
  const canDisable = activeCount > minManagedInstanceCount &&
    !isRunPlanWorkerBusy(highestActiveInstance) &&
    !controlsLocked;
  const buttons = [
    activeCount < limit
      ? `<button class="laneWorkerButton" type="button" data-active-instance-count="${activeCount + 1}" title="Add active instance" aria-label="Set active recording instances to ${activeCount + 1}"${disabledAttr(!canEnable)}>+</button>`
      : '',
    activeCount > minManagedInstanceCount
      ? `<button class="laneWorkerButton" type="button" data-active-instance-count="${activeCount - 1}" title="Remove active instance" aria-label="Set active recording instances to ${activeCount - 1}"${disabledAttr(!canDisable)}>-</button>`
      : ''
  ].join('');

  return `
    <div class="laneWorkerControls" aria-label="${escapeHtml(name)} worker controls">
      ${buttons}
    </div>
  `;
}

function renderRunPlanLane(lane, visibleQueue, totalSeconds) {
  const index = lane.index;
  const instance = lane.instance;
  const label = formatInstanceDisplayName(instance, index);
  const enabled = !instance || isInstanceEnabledForDisplay(instance);
  const subtitle = formatRunPlanLaneSubtitle(instance, enabled);
  const cards = lane.items
    .map(scheduled => renderRunPlanCard(scheduled, visibleQueue, index, totalSeconds))
    .join('');

  return `
    <div class="runPlanLane ${enabled ? '' : 'disabledLane'}">
      <div class="laneLabel">
        <div class="laneLabelText">
          <div class="laneLabelNameRow">
            <strong>${escapeHtml(label)}</strong>
            <span class="statusDot ${enabled && hasLiveInstanceSignal(instance) ? 'good' : 'warn'}" aria-hidden="true"></span>
          </div>
          <span>${escapeHtml(subtitle)}</span>
        </div>
        ${renderRunPlanWorkerControls(instance, index)}
      </div>
      <div class="laneTrack">
        <div class="laneLine" aria-hidden="true"></div>
        <div class="laneCards" data-run-plan-card-track style="--timeline-seconds: ${Number(totalSeconds).toFixed(3)}">${cards}</div>
      </div>
    </div>
  `;
}

function renderRunPlanCard(scheduled, visibleQueue, laneIndex, totalSeconds) {
  const item = scheduled.item;
  const queue = state.queue || [];
  const index = queue.findIndex(candidate => candidate.id === item.id);
  const selected = selectedQueueId === item.id;
  const tone = runPlanItemTone(item);
  const queued = tone === 'queued';
  const active = tone === 'active';
  const completed = tone === 'complete';
  const failed = tone === 'failed';
  const warning = tone === 'warning';
  const canDrag = !active;
  const className = [
    'runPlanCard',
    `tone-${tone}`,
    selected ? 'selected' : '',
    queued ? 'queued' : '',
    active ? 'active' : '',
    completed ? 'complete' : '',
    failed ? 'failed' : '',
    warning ? 'warning' : ''
  ].filter(Boolean).join(' ');
  const startRatio = scheduled.startSeconds / Math.max(1, totalSeconds);
  const widthRatio = scheduled.durationSeconds / Math.max(1, totalSeconds);

  return `
    <article class="${className}" data-select-id="${escapeHtml(item.id)}" data-drag-id="${escapeHtml(item.id)}" data-start-seconds="${Number(scheduled.startSeconds).toFixed(3)}" data-duration-seconds="${Number(scheduled.durationSeconds).toFixed(3)}" data-start-lead-in-seconds="${Number(scheduled.startLeadInSeconds || 0).toFixed(3)}" data-gap-after-seconds="${Number(scheduled.gapAfterSeconds || 0).toFixed(3)}" draggable="${canDrag ? 'true' : 'false'}" style="--card-left: ${formatPercent(startRatio)}; --card-width: ${formatPercent(widthRatio)}">
      ${renderPlanCover(item)}
      <div class="runPlanCardMain">
        <div class="cardTitleRow">
          <strong>${escapeHtml(item.songName || item.fileName)}</strong>
          <span>${formatSeconds(scheduled.durationSeconds)}</span>
        </div>
        <span class="queueMeta">${escapeHtml(renderRunPlanCardSubtitle(item))}</span>
        <div class="planPillRow">
          <span class="planPill provider ${providerPillClass(item)}">${escapeHtml(formatProviderPill(item))}</span>
          <span class="planPill ${mapStatusClass(item.mapStatus)}">${escapeHtml(formatPlanMapStatus(item))}</span>
          <span class="planPill sync">${escapeHtml(formatPlanSync(item))}</span>
        </div>
      </div>
    </article>
  `;
}

function renderTimelineItem(item) {
  const tone = runPlanItemTone(item);
  return `
    <button class="timelineItem tone-${tone} ${selectedQueueId === item.id ? 'selected' : ''}" type="button" data-select-id="${escapeHtml(item.id)}">
      <span>${escapeHtml(item.sequenceNumber)}</span>
      ${renderPlanCover(item)}
    </button>
  `;
}

function bindRunPlanActions(board) {
  board.querySelectorAll('[data-select-id]').forEach(element => {
    element.addEventListener('click', event => {
      const selectId = element.dataset.selectId;
      selectedQueueId = selectedQueueId === selectId ? null : selectId;
      editingQueueId = null;
      renderQueue();
    });
  });

  board.querySelectorAll('[data-active-instance-count]').forEach(button => {
    button.addEventListener('click', event => {
      event.preventDefault();
      event.stopPropagation();
      const count = Number(button.dataset.activeInstanceCount);
      if (!Number.isFinite(count)) return;
      runAction(() => setActiveRecordingInstanceCount(count));
    });
  });

  const clearSelection = () => {
    if (!selectedQueueId && !editingQueueId) return;
    selectedQueueId = null;
    editingQueueId = null;
    renderQueue();
  };

  board.querySelectorAll('.runPlanSchedule, .timelineList').forEach(container => {
    container.addEventListener('click', event => {
      if (event.target.closest('[data-select-id], [data-active-instance-count]')) return;
      clearSelection();
    });
  });
}

function bindRunPlanDrag(board) {
  board.querySelectorAll('.runPlanCard[draggable="true"]').forEach(card => {
    card.addEventListener('dragstart', event => {
      draggedQueueId = card.dataset.dragId || null;
      card.classList.add('dragging');
      if (event.dataTransfer) {
        event.dataTransfer.effectAllowed = 'move';
        event.dataTransfer.setData('text/plain', draggedQueueId || '');
      }
    });

    card.addEventListener('dragend', () => {
      draggedQueueId = null;
      clearRunPlanDropMarkers(board);
    });

    card.addEventListener('dragover', event => {
      if (!draggedQueueId || draggedQueueId === card.dataset.dragId) return;
      event.preventDefault();
      if (event.dataTransfer) event.dataTransfer.dropEffect = 'move';
      updateRunPlanDropMarker(card, event);
    });

    card.addEventListener('dragleave', event => {
      if (!event.relatedTarget || !card.contains(event.relatedTarget)) {
        card.classList.remove('dropBefore', 'dropAfter');
      }
    });

    card.addEventListener('drop', event => {
      if (!draggedQueueId && isFileTransfer(event.dataTransfer)) return;
      event.preventDefault();
      event.stopPropagation();
      const sourceId = draggedQueueId || event.dataTransfer?.getData('text/plain') || '';
      const targetId = card.dataset.dragId || '';
      const placeAfter = isDropAfter(card, event);
      draggedQueueId = null;
      clearRunPlanDropMarkers(board);
      moveQueueItemByDrag(sourceId, targetId, placeAfter);
    });
  });
}

function updateRunPlanDropMarker(card, event) {
  const after = isDropAfter(card, event);
  card.classList.toggle('dropBefore', !after);
  card.classList.toggle('dropAfter', after);
}

function isDropAfter(card, event) {
  const rect = card.getBoundingClientRect();
  return event.clientX > rect.left + rect.width / 2;
}

function clearRunPlanDropMarkers(root = document) {
  root.querySelectorAll('.runPlanCard.dropBefore, .runPlanCard.dropAfter, .runPlanCard.dragging')
    .forEach(card => card.classList.remove('dropBefore', 'dropAfter', 'dragging'));
}

async function moveQueueItemByDrag(sourceId, targetId, placeAfter) {
  if (!sourceId || !targetId || sourceId === targetId) return;
  const queue = state.queue || [];
  const sourceIndex = queue.findIndex(item => item.id === sourceId);
  const targetIndex = queue.findIndex(item => item.id === targetId);
  if (sourceIndex < 0 || targetIndex < 0) return;

  let destinationIndex = targetIndex + (placeAfter ? 1 : 0);
  if (sourceIndex < destinationIndex) destinationIndex--;
  destinationIndex = Math.max(0, Math.min(queue.length - 1, destinationIndex));
  if (destinationIndex === sourceIndex) return;

  const action = destinationIndex < sourceIndex ? 'move-up' : 'move-down';
  const steps = Math.abs(destinationIndex - sourceIndex);
  await runAction(async () => {
    for (let index = 0; index < steps; index++) {
      await postJson(queueUrl(sourceId, action));
    }
    selectedQueueId = sourceId;
    renderQueue();
    showToast('Replay moved');
  });
}

function renderPlanCover(item) {
  return `
    <span class="planCover" aria-hidden="true">
      ${item.coverArtUrl ? `<img src="${escapeHtml(item.coverArtUrl)}" alt="" onerror="this.remove()">` : ''}
      <span>${escapeHtml(initials(item.songName || item.fileName))}</span>
    </span>
  `;
}

function formatPlanMapStatus(item) {
  if (sameStatus(item.mapStatus, 'Found') || sameStatus(item.mapStatus, 'Downloaded')) return 'Map OK';
  return formatMapStatus(item);
}

function formatPlanSync(item) {
  if (Number.isFinite(Number(item.syncCorrectionMilliseconds))) {
    return `${formatSignedNumber(Number(item.syncCorrectionMilliseconds).toFixed(1))} ms`;
  }

  if (item.syncStatus) return item.syncStatus;
  return 'Sync pending';
}

function isSyncOutOfRange(item) {
  const offset = Number(item.syncCorrectionMilliseconds);
  return Number.isFinite(offset) && Math.abs(offset) >= 100;
}

function renderQueueDetails() {
  const details = document.getElementById('queueDetails');
  if (!details) return;
  const item = (state.queue || []).find(candidate => candidate.id === selectedQueueId);
  details.innerHTML = '';

  if (!item) {
    details.hidden = true;
    return;
  }
  details.hidden = false;

  const index = state.queue.findIndex(candidate => candidate.id === item.id);
  const active = isActiveStatus(item.status);
  const queued = sameStatus(item.status, 'Queued');
  const canOpen = canOpenRecording(item);
  const pathText = item.error || item.warning || item.outputPath || '';
  const pathClass = item.error ? 'detailPath errorText' : (item.warning ? 'detailPath warningText' : 'detailPath');
  const mapMissing = sameStatus(item.mapStatus, 'Missing');

  details.innerHTML = `
    <div class="inspectorHero">
      <div class="inspectorThumb" aria-hidden="true">
        ${item.coverArtUrl ? `<img src="${escapeHtml(item.coverArtUrl)}" alt="" onerror="this.remove()">` : ''}
        <span>${escapeHtml(initials(item.songName || item.fileName))}</span>
      </div>
      <div class="inspectorMain">
        <div class="detailsHeader">
          <div class="detailsTitleBlock">
            <span class="detailIndex">#${item.sequenceNumber}</span>
            <h3>${escapeHtml(item.songName || item.fileName)}</h3>
            <p>${escapeHtml(renderRunPlanCardSubtitle(item))}</p>
          </div>
          <span class="badge ${statusClass(item.status)}">${escapeHtml(item.status)}</span>
        </div>
        <dl class="detailGrid">
          <div class="detailMetric"><dt>Player</dt><dd>${escapeHtml(formatPlayerName(item.playerName) || '-')}</dd></div>
          <div class="detailMetric"><dt>Provider</dt><dd>${escapeHtml(formatProvider(item))}</dd></div>
          <div class="detailMetric"><dt>Difficulty</dt><dd>${escapeHtml(item.difficulty || '-')}</dd></div>
          <div class="detailMetric"><dt>Length</dt><dd>${formatSeconds(item.estimatedSeconds)}</dd></div>
        </dl>
        ${renderMapStatusDetail(item)}
        ${pathText
        ? `<div class="${pathClass}">${item.error ? '<strong>Failure reason</strong>' : (item.warning ? '<strong>Warning</strong>' : '')}<span>${escapeHtml(pathText)}</span></div>`
        : ''}
        ${renderSyncMarkerResult(item)}
        ${renderCalibrationResult(item)}
      </div>
      <div class="detailsActions">
        <button class="actionButton" type="button" data-detail-move-up${disabledAttr(active || index === 0)}>${icon('up')}<span>Move earlier</span></button>
        <button class="actionButton" type="button" data-detail-move-down${disabledAttr(active || index === state.queue.length - 1)}>${icon('down')}<span>Move later</span></button>
        <button class="actionButton" type="button" data-detail-open${disabledAttr(!canOpen)}>${icon('open')}<span>Open recording</span></button>
        ${mapMissing ? `
          <button class="actionButton" type="button" data-detail-download-map${disabledAttr(active)}>${icon('download')}<span>Repair / download map</span></button>
          <button class="actionButton primaryAction" type="button" data-detail-upload-map${disabledAttr(active)}>${icon('upload')}<span>Upload map</span></button>
        ` : ''}
        <button class="actionButton" type="button" data-detail-requeue${disabledAttr(active || queued)}>${icon('refresh')}<span>Requeue</span></button>
        <button class="actionButton dangerAction" type="button" data-detail-remove${disabledAttr(active)}>${icon('trash')}<span>Remove</span></button>
      </div>
      <input class="hiddenFileInput" type="file" accept=".zip" data-map-file>
    </div>
  `;

  bindQueueDetailsActions(details, item);
}

function syncQueueSelection(queue, visibleQueue) {
  const selectedExists = queue.some(item => item.id === selectedQueueId);
  const selectedVisible = visibleQueue.some(item => item.id === selectedQueueId);
  if (!selectedExists || !selectedVisible) {
    selectedQueueId = null;
  }

  if (editingQueueId && editingQueueId !== selectedQueueId) {
    editingQueueId = null;
  }
}

function renderQueueSubtitle(item) {
  const parts = [];
  if (item.mapper) parts.push(item.mapper);
  if (item.fileName && item.mapper !== item.fileName) parts.push(item.fileName);
  return parts.join(' | ') || item.fileName || 'Replay';
}

function renderRunPlanCardSubtitle(item) {
  const parts = [];
  if (item.mapper) parts.push(item.mapper);
  else if (item.playerName) parts.push(formatPlayerName(item.playerName));
  if (item.difficulty) parts.push(item.difficulty);
  return parts.filter(Boolean).join(' | ') || 'Replay';
}

function formatPlayerName(value) {
  return String(value ?? '')
    .replace(/<[^>]*>/g, '')
    .trim();
}

function formatProvider(item) {
  if (Number(item.provider) === 3) return 'ScoreSaber';
  if (Number(item.provider) === 1) return 'BeatLeader';
  const provider = String(item.provider || '').toLowerCase();
  if (provider.includes('scoresaber')) return 'ScoreSaber';
  if (provider.includes('beatleader')) return 'BeatLeader';
  return item.replayFormat || 'Replay';
}

function formatProviderPill(item) {
  const provider = formatProvider(item).toLowerCase();
  if (provider === 'scoresaber') return 'SS';
  if (provider === 'beatleader') return 'BL';
  return formatProvider(item);
}

function providerPillClass(item) {
  const provider = formatProvider(item).toLowerCase();
  if (provider === 'scoresaber') return 'scoreSaber';
  if (provider === 'beatleader') return 'beatLeader';
  return '';
}

function formatMapStatus(item) {
  const status = item.mapStatus || 'Unchecked';
  if (sameStatus(status, 'Downloading')) return 'Checking map';
  if (sameStatus(status, 'Missing')) return 'Map missing';
  return 'Map unchecked';
}

function shouldShowMapStatus(item) {
  const status = item.mapStatus || 'Unchecked';
  return sameStatus(status, 'Missing') ||
    sameStatus(status, 'Downloading') ||
    sameStatus(status, 'Unchecked');
}

function renderMapMiniStatus(item) {
  if (!shouldShowMapStatus(item)) return '';
  return `<span class="mapMiniStatus ${mapStatusClass(item.mapStatus)}">${escapeHtml(formatMapStatus(item))}</span>`;
}

function mapStatusClass(status) {
  if (sameStatus(status, 'Found') || sameStatus(status, 'Downloaded')) return 'mapOk';
  if (sameStatus(status, 'Downloading')) return 'mapChecking';
  if (sameStatus(status, 'Missing')) return 'mapMissing';
  return 'mapUnchecked';
}

function renderMapStatusDetail(item) {
  if (!shouldShowMapStatus(item)) return '';
  const detail = item.mapStatusDetail || '';
  const installPath = item.mapInstallPath || '';
  if (!detail && !installPath) return '';
  return `
    <div class="mapStatusDetail ${mapStatusClass(item.mapStatus)}">
      <strong>${escapeHtml(formatMapStatus(item))}</strong>
      ${detail ? `<span>${escapeHtml(detail)}</span>` : ''}
      ${installPath ? `<small>${escapeHtml(installPath)}</small>` : ''}
    </div>
  `;
}

function renderQueuePathLine(item) {
  const text = item.outputPath || item.warning || item.error || '';
  if (!text) return '';
  const className = item.error ? 'queuePath errorText' : (item.warning ? 'queuePath warningText' : 'queuePath outputPath');
  return `<span class="${className}">${escapeHtml(text)}</span>`;
}

function assignedInstanceText(item) {
  return item.assignedInstance === null || item.assignedInstance === undefined
    ? '-'
    : formatInstanceName(item.assignedInstance);
}

function canOpenRecording(item) {
  return sameStatus(item.status, 'Completed') && Boolean(item.outputPath);
}

function disabledAttr(disabled) {
  return disabled ? ' disabled' : '';
}

function bindQueueRowActions(row) {
  const moveUp = row.querySelector('[data-move-up]');
  const moveDown = row.querySelector('[data-move-down]');
  const open = row.querySelector('[data-open]');
  const select = row.querySelector('[data-select-id]');
  const selectId = select?.dataset.selectId;

  if (moveUp) moveUp.addEventListener('click', () => queueAction(moveUp.dataset.moveUp, 'move-up', 'Replay moved up'));
  if (moveDown) moveDown.addEventListener('click', () => queueAction(moveDown.dataset.moveDown, 'move-down', 'Replay moved down'));
  if (open) open.addEventListener('click', () => openRecordedFile(open.dataset.open));
  row.addEventListener('click', event => {
    if (!selectId || event.target.closest('.queueItemControls')) return;
    selectedQueueId = selectedQueueId === selectId ? null : selectId;
    editingQueueId = null;
    renderQueue();
  });
}

function bindQueueDetailsActions(details, item) {
  const moveUp = details.querySelector('[data-detail-move-up]');
  const moveDown = details.querySelector('[data-detail-move-down]');
  const open = details.querySelector('[data-detail-open]');
  const downloadMap = details.querySelector('[data-detail-download-map]');
  const uploadMap = details.querySelector('[data-detail-upload-map]');
  const mapFile = details.querySelector('[data-map-file]');
  const requeue = details.querySelector('[data-detail-requeue]');
  const remove = details.querySelector('[data-detail-remove]');

  if (moveUp) moveUp.addEventListener('click', () => queueAction(item.id, 'move-up', 'Replay moved up'));
  if (moveDown) moveDown.addEventListener('click', () => queueAction(item.id, 'move-down', 'Replay moved down'));
  if (open) open.addEventListener('click', () => openRecordedFile(item.id));
  if (downloadMap) downloadMap.addEventListener('click', () => queueAction(item.id, 'map/download', 'Map check finished'));
  if (uploadMap && mapFile) {
    uploadMap.addEventListener('click', () => mapFile.click());
    mapFile.addEventListener('change', () => uploadQueueMap(item.id, mapFile.files?.[0] || null));
  }
  if (requeue) requeue.addEventListener('click', () => queueAction(item.id, 'requeue', 'Replay requeued'));
  if (remove) remove.addEventListener('click', () => queueAction(item.id, 'remove', 'Replay removed'));
}

function renderSyncMarkerResult(item) {
  if (!item.syncStatus) return '';

  const status = item.syncStatus || 'Unknown';
  const correction = Number.isFinite(Number(item.syncCorrectionMilliseconds))
    ? `Correction ${formatSignedNumber(Number(item.syncCorrectionMilliseconds).toFixed(1))} ms`
    : 'Correction unavailable';
  const trim = Number.isFinite(Number(item.trimStartSeconds))
    ? `Trim ${formatNumber(item.trimStartSeconds)}s`
    : 'Trim unavailable';

  return `
    <div class="syncSummary">
      <div class="syncHeader">
        <span class="badge ${statusClass(status)}">${escapeHtml(status)}</span>
        <span class="cellTitle">${escapeHtml(correction)}</span>
        <span class="muted">${escapeHtml(trim)}</span>
      </div>
      ${item.syncReportPath ? `<div class="detailPath">${escapeHtml(item.syncReportPath)}</div>` : ''}
    </div>
  `;
}

function renderCalibrationResult(item) {
  const calibration = item.calibration || {};
  if (!calibration.updatedAtUtc && (!calibration.status || calibration.status === 'Unset')) return '';

  const offset = Number.isFinite(Number(calibration.syncOffsetMilliseconds))
    ? `${formatSignedNumber(Number(calibration.syncOffsetMilliseconds).toFixed(1))} ms`
    : 'Offset unset';
  const trim = Number.isFinite(Number(calibration.trimStartSeconds))
    ? `Trim ${formatNumber(calibration.trimStartSeconds)}s`
    : 'Trim unset';
  const updated = calibration.updatedAtUtc ? `Updated ${formatEventTime(calibration.updatedAtUtc)}` : 'Not saved';

  return `
    <div class="syncSummary calibrationSummary">
      <div class="syncHeader">
        <span class="badge ${statusClass(calibration.status || 'Manual')}">${escapeHtml(calibration.status || 'Manual')}</span>
        <span class="cellTitle">${escapeHtml(offset)}</span>
        <span class="muted">${escapeHtml(`${trim} | ${updated}`)}</span>
      </div>
      ${calibration.notes ? `<div class="detailPath">${escapeHtml(calibration.notes)}</div>` : ''}
    </div>
  `;
}

async function queueAction(id, action, message) {
  if (!id) return;
  await runAction(async () => {
    await postJson(queueUrl(id, action));
    showToast(message);
  });
}

async function renameCompletedQueueRecordings(format = getRecordingNameFormat()) {
  const response = await fetch('/api/queue/rename-completed-recordings', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ format })
  });
  if (!response.ok) throw new Error(await response.text());

  const result = await response.json();
  state = result.state || state;
  render();

  const renamed = Number(result.renamedCount) || 0;
  const skipped = Number(result.skippedCount) || 0;
  if (!renamed) {
    showToast(skipped ? `No recordings renamed, ${skipped} skipped` : 'No completed recordings to rename');
    return;
  }

  showToast(`Renamed ${renamed} recording${renamed === 1 ? '' : 's'}${skipped ? `, ${skipped} skipped` : ''}`);
}

async function renameSelectedCollectionRecordings(format = getRecordingNameFormat()) {
  const collectionId = getSelectedCollectionId();
  if (!collectionId) {
    showToast('Choose a collection');
    return;
  }

  const response = await fetch(`/api/collections/${encodeURIComponent(collectionId)}/rename-recordings`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ format })
  });
  if (!response.ok) throw new Error(await response.text());

  const result = await response.json();
  state = result.state || state;
  render();

  const renamed = Number(result.renamedCount) || 0;
  const skipped = Number(result.skippedCount) || 0;
  if (!renamed) {
    showToast(skipped ? `No recordings renamed, ${skipped} skipped` : 'No recordings to rename');
    return;
  }

  showToast(`Renamed ${renamed} recording${renamed === 1 ? '' : 's'}${skipped ? `, ${skipped} skipped` : ''}`);
}

function getRecordingNameFormat() {
  return recordingNameFormat || 'Default';
}

function syncRecordingNameFormatControls(source = null) {
  const sourceValue = source?.value;
  if (sourceValue) recordingNameFormat = sourceValue;

  for (const option of getRecordingNameFormatElements()) {
    option.checked = option.value === recordingNameFormat;
  }
}

function getRecordingNameFormatElements() {
  return [...document.querySelectorAll('input[name="recordingNameFormat"]')];
}

function getSelectedCollectionName() {
  const collectionId = getSelectedCollectionId();
  const collection = (state?.collections || []).find(item => item.id === collectionId);
  return collection?.name || 'Selected collection';
}

function selectedCollectionHasCompletedRecordings() {
  const collectionId = getSelectedCollectionId();
  const collection = (state?.collections || []).find(item => item.id === collectionId);
  return Boolean(collection?.items?.some(item => item.completedOutputPath));
}

async function openRecordingRenameModal(context) {
  const queue = state?.queue || [];
  if (context === 'queue' && !queue.some(item => sameStatus(item.status, 'Completed'))) {
    showToast('No completed recordings to rename');
    return;
  }

  if (context === 'collection') {
    if (!getSelectedCollectionId()) {
      showToast('Choose a collection');
      return;
    }

    if (!selectedCollectionHasCompletedRecordings()) {
      showToast('No collection recordings to rename');
      return;
    }
  }

  const modal = document.getElementById('recordingRenameModal');
  if (!modal) return;

  recordingRenameContext = context;
  recordingRenameLastFocus = document.activeElement;
  syncRecordingNameFormatControls();
  setText('recordingRenameKicker', context === 'collection' ? 'Collection Recordings' : 'Queue Recordings');
  setText('recordingRenameScope', context === 'collection'
    ? getSelectedCollectionName()
    : 'Completed queue recordings');
  applyRecordingRenamePreview(await loadRecordingRenamePreview(context));
  modal.hidden = false;
  document.querySelector('input[name="recordingNameFormat"]:checked')?.focus();
}

async function loadRecordingRenamePreview(context) {
  const fallback = buildRecordingRenamePreviewFromState(context);
  const url = getRecordingRenamePreviewUrl(context);
  if (!url) return fallback;

  try {
    const response = await fetch(url);
    if (!response.ok) return fallback;
    const preview = await response.json();
    return {
      sourceLabel: cleanQueueExportField(preview?.sourceLabel) || fallback.sourceLabel,
      examples: {
        ...fallback.examples,
        ...(preview?.examples || {})
      }
    };
  } catch {
    return fallback;
  }
}

function getRecordingRenamePreviewUrl(context) {
  if (context === 'collection') {
    const collectionId = getSelectedCollectionId();
    return collectionId
      ? `/api/collections/${encodeURIComponent(collectionId)}/recording-name-preview`
      : '';
  }

  return '/api/queue/recording-name-preview';
}

function applyRecordingRenamePreview(preview) {
  const examples = preview?.examples || {};
  for (const element of document.querySelectorAll('[data-recording-rename-example]')) {
    const format = element.dataset.recordingRenameExample;
    element.textContent = cleanQueueExportField(examples[format]) ||
      recordingRenameFallbackExamples[format] ||
      'Example unavailable';
  }
}

function buildRecordingRenamePreviewFromState(context) {
  const item = getRecordingRenamePreviewItem(context);
  const fields = getRecordingRenamePreviewFields(item);
  return {
    sourceLabel: fields.song,
    examples: {
      Default: joinRecordingRenamePreviewParts(
        `${String(fields.sequence).padStart(3, '0')} - ${fields.song}${fields.rawDifficulty ? ` [${fields.rawDifficulty}]` : ''}`),
      Key: fields.key,
      KeySong: joinRecordingRenamePreviewParts(fields.key, fields.song),
      Song: fields.song,
      SongArtist: joinRecordingRenamePreviewParts(fields.song, fields.artist),
      SongArtistPlayer: joinRecordingRenamePreviewParts(fields.song, fields.artist, fields.player),
      SongPlayer: joinRecordingRenamePreviewParts(fields.song, fields.player),
      SongMapper: joinRecordingRenamePreviewParts(fields.song, fields.mapper),
      SongDifficulty: joinRecordingRenamePreviewParts(fields.song, fields.displayDifficulty),
      PlayerSong: joinRecordingRenamePreviewParts(fields.player, fields.song)
    }
  };
}

function getRecordingRenamePreviewItem(context) {
  if (context === 'collection') {
    const collectionId = getSelectedCollectionId();
    const collection = (state?.collections || []).find(item => item.id === collectionId);
    return collection?.items?.find(item => item.completedOutputPath || item.completedAtUtc) ||
      collection?.items?.[0] ||
      null;
  }

  const queue = state?.queue || [];
  return queue.find(item => sameStatus(item.status, 'Completed') || item.outputPath || item.completedAtUtc) ||
    queue[0] ||
    null;
}

function getRecordingRenamePreviewFields(item) {
  const song = cleanQueueExportField(item?.songName) ||
    cleanQueueExportField(item?.fileName) ||
    'Song';
  const mapper = cleanQueueExportField(item?.mapper) || 'Mapper';
  const artist = cleanQueueExportField(item?.artist) ||
    inferArtistFromMapInstallPath(item?.mapInstallPath) ||
    'Artist';
  const player = cleanQueueExportField(formatPlayerName(item?.playerName)) || 'Player';
  const rawDifficulty = cleanQueueExportField(item?.difficulty);
  const displayDifficulty = displayRenamePreviewDifficulty(rawDifficulty) || 'Difficulty';
  return {
    sequence: Number.isFinite(Number(item?.sequenceNumber)) ? Number(item.sequenceNumber) : 1,
    key: extractRecordingPreviewKey(item) || 'Key',
    song,
    artist,
    mapper,
    player,
    rawDifficulty,
    displayDifficulty
  };
}

function joinRecordingRenamePreviewParts(...parts) {
  return parts
    .map(part => cleanQueueExportField(part))
    .filter(Boolean)
    .join(' - ');
}

function extractRecordingPreviewKey(item) {
  const directKey = cleanQueueExportField(item?.beatSaverKey);
  if (directKey) return directKey;

  const beatSaverUrlKey = extractBeatSaverKeyFromUrl(item?.sourceUrl);
  if (beatSaverUrlKey) return beatSaverUrlKey;

  const installPathKey = extractBeatSaverKeyFromInstallPath(item?.mapInstallPath);
  if (installPathKey) return installPathKey;

  return cleanQueueExportField(item?.levelHash);
}

function extractBeatSaverKeyFromUrl(url) {
  const text = cleanQueueExportField(url);
  if (!text || !/beatsaver/i.test(text)) return '';

  try {
    const parsed = new URL(text);
    const segments = parsed.pathname.split('/').filter(Boolean);
    for (let index = 0; index < segments.length - 1; index++) {
      if (/^(beatmap|maps)$/i.test(segments[index])) {
        return cleanQueueExportField(segments[index + 1]);
      }
    }
    return cleanQueueExportField(segments.at(-1));
  } catch {
    return '';
  }
}

function extractBeatSaverKeyFromInstallPath(path) {
  const name = cleanQueueExportField(String(path ?? '').split(/[\\/]/).filter(Boolean).pop());
  const match = name.match(/^([0-9a-f]{3,8})(?:\s|\(|$)/i);
  return match?.[1] || '';
}

function inferArtistFromMapInstallPath(path) {
  const name = cleanQueueExportField(String(path ?? '').split(/[\\/]/).filter(Boolean).pop());
  const match = name.match(/\(([^)]+)\)\s*$/);
  if (!match) return '';
  return cleanQueueExportField(match[1].split(' - ')[0]);
}

function displayRenamePreviewDifficulty(value) {
  const text = cleanQueueExportField(value);
  const normalized = text.replace(/[^a-z0-9]+/gi, '').toLowerCase();
  if (normalized === 'expertplus' || /expert\+/i.test(text)) return 'Expert+';
  if (normalized === 'expert') return 'Expert';
  if (normalized === 'hard') return 'Hard';
  if (normalized === 'normal') return 'Normal';
  if (normalized === 'easy') return 'Easy';
  return text;
}

function closeRecordingRenameModal() {
  const modal = document.getElementById('recordingRenameModal');
  if (!modal) return;
  modal.hidden = true;
  recordingRenameContext = null;
  if (recordingRenameLastFocus && typeof recordingRenameLastFocus.focus === 'function') {
    recordingRenameLastFocus.focus();
  }
  recordingRenameLastFocus = null;
}

async function applyRecordingRenameSelection() {
  const context = recordingRenameContext;
  const format = getRecordingNameFormat();
  if (context === 'collection') {
    await renameSelectedCollectionRecordings(format);
  } else {
    await renameCompletedQueueRecordings(format);
  }
  closeRecordingRenameModal();
}

async function uploadQueueMap(id, file) {
  if (!id || !file) return;
  if (!file.name.toLowerCase().endsWith('.zip')) {
    showToast('Choose a song .zip');
    return;
  }

  await runAction(async () => {
    const form = new FormData();
    form.append('file', file);
    const response = await fetch(queueUrl(id, 'map/upload'), { method: 'POST', body: form });
    if (!response.ok) throw new Error(await response.text());
    state = await response.json();
    render();
    showToast('Map uploaded');
  });
}

function createQueueExportContext(overrides = {}) {
  return {
    source: 'queue',
    items: state?.queue || [],
    kicker: 'Queue Export',
    title: 'Replay List',
    emptyToast: 'No replays to export',
    copiedToast: 'Queue list copied',
    readyToast: 'Queue export ready',
    countPrefix: '',
    ...overrides
  };
}

function getQueueExportContext() {
  if (!queueExportContext) {
    queueExportContext = createQueueExportContext();
  }
  return queueExportContext;
}

function buildQueueExportText(format = getQueueExportFormat(), items = getQueueExportContext().items) {
  const queue = Array.isArray(items) ? items : [];
  if (format === 'spreadsheet') {
    return [
      ['Song', 'Player', 'Difficulty', 'Mapper', 'Provider'].join('\t'),
      ...queue.map(item => {
        const fields = getQueueExportFields(item);
        return [
          fields.songName,
          fields.playerName,
          fields.difficulty,
          fields.mapper,
          fields.provider
        ].map(formatTsvField).join('\t');
      })
    ].join('\n');
  }

  if (format === 'csv') {
    return [
      ['Song', 'Player', 'Difficulty', 'Mapper', 'Provider'].map(formatCsvField).join(','),
      ...queue.map(item => {
        const fields = getQueueExportFields(item);
        return [
          fields.songName,
          fields.playerName,
          fields.difficulty,
          fields.mapper,
          fields.provider
        ].map(formatCsvField).join(',');
      })
    ].join('\n');
  }

  return queue.map(item => formatQueueExportLine(item, format)).join('\n');
}

function formatQueueExportLine(item, format = 'song-player') {
  const fields = getQueueExportFields(item);
  const difficulty = fields.difficulty ? ` [${fields.difficulty}]` : '';
  const mapper = fields.mapper ? ` - Mapper: ${fields.mapper}` : '';

  if (format === 'numbered-song-player') {
    return `${fields.sequenceNumber}. ${fields.songName} - Played By: ${fields.playerName}`;
  }

  if (format === 'song-difficulty-player') {
    return `${fields.songName}${difficulty} - Played By: ${fields.playerName}`;
  }

  if (format === 'player-song') {
    return `${fields.playerName} - ${fields.songName}`;
  }

  if (format === 'song-mapper-player') {
    return `${fields.songName}${mapper} - Played By: ${fields.playerName}`;
  }

  return `${fields.songName} - Played By: ${fields.playerName}`;
}

function getQueueExportFields(item) {
  const songName = cleanQueueExportField(item?.songName) ||
    cleanQueueExportField(item?.fileName) ||
    `Replay #${item?.sequenceNumber || '?'}`;
  return {
    sequenceNumber: Number.isFinite(Number(item?.sequenceNumber)) ? Number(item.sequenceNumber) : '?',
    songName,
    playerName: cleanQueueExportField(formatPlayerName(item?.playerName)) || 'Unknown',
    difficulty: cleanQueueExportField(item?.difficulty),
    mapper: cleanQueueExportField(item?.mapper),
    provider: cleanQueueExportField(formatProvider(item))
  };
}

function cleanQueueExportField(value) {
  return String(value ?? '')
    .replace(/<[^>]*>/g, '')
    .replace(/[\r\n\t]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim();
}

function formatTsvField(value) {
  return cleanQueueExportField(value);
}

function formatCsvField(value) {
  const text = cleanQueueExportField(value);
  return `"${text.replaceAll('"', '""')}"`;
}

function getQueueExportFormat() {
  return document.getElementById('queueExportFormat')?.value || 'song-player';
}

function refreshQueueExportText() {
  const context = getQueueExportContext();
  const items = Array.isArray(context.items) ? context.items : [];
  const text = buildQueueExportText(getQueueExportFormat(), items);
  const textarea = document.getElementById('queueExportText');
  if (textarea) textarea.value = text;
  const count = items.length || 0;
  const prefix = context.countPrefix || '';
  setText('queueExportCount', count ? `${prefix}${count} replay${count === 1 ? '' : 's'}` : 'No replays');
  return text;
}

async function openQueueExportModal(copyOnOpen = true, context = null) {
  queueExportContext = createQueueExportContext(context || {});
  const items = Array.isArray(queueExportContext.items) ? queueExportContext.items : [];
  if (!items.length) {
    showToast(queueExportContext.emptyToast || 'No replays to export');
    return;
  }

  const modal = document.getElementById('queueExportModal');
  const textarea = document.getElementById('queueExportText');
  if (!modal || !textarea) return;

  queueExportLastFocus = document.activeElement;
  setText('queueExportKicker', queueExportContext.kicker || 'Queue Export');
  setText('queueExportTitle', queueExportContext.title || 'Replay List');
  modal.hidden = false;
  const text = refreshQueueExportText();
  textarea.focus();
  textarea.select();

  if (copyOnOpen) {
    const copied = await copyTextToClipboard(text);
    showToast(copied
      ? (queueExportContext.copiedToast || 'Queue list copied')
      : (queueExportContext.readyToast || 'Queue export ready'));
  }
}

function closeQueueExportModal() {
  const modal = document.getElementById('queueExportModal');
  if (!modal) return;
  modal.hidden = true;
  if (queueExportLastFocus && typeof queueExportLastFocus.focus === 'function') {
    queueExportLastFocus.focus();
  }
  queueExportLastFocus = null;
  queueExportContext = null;
}

function selectQueueExportText() {
  const textarea = document.getElementById('queueExportText');
  if (!textarea) return;
  textarea.focus();
  textarea.select();
}

async function copyQueueExportText() {
  const context = getQueueExportContext();
  const text = refreshQueueExportText();
  selectQueueExportText();
  const copied = await copyTextToClipboard(text);
  showToast(copied ? (context.copiedToast || 'Queue list copied') : 'Select the text and copy it');
}

async function openCollectionReplayListExportModal(copyOnOpen = true) {
  const collectionId = getSelectedCollectionId();
  const collection = (state?.collections || []).find(item => item.id === collectionId);
  if (!collection) {
    showToast('Choose a collection');
    return;
  }

  const items = Array.isArray(collection.items) ? collection.items : [];
  await openQueueExportModal(copyOnOpen, {
    source: 'collection',
    items,
    kicker: 'Collection Export',
    title: 'Replay List',
    emptyToast: 'No replays in this collection',
    copiedToast: 'Collection replay list copied',
    readyToast: 'Collection replay list ready',
    countPrefix: collection.name ? `${collection.name}: ` : ''
  });
}

async function copyTextToClipboard(text) {
  if (!text) return false;

  if (navigator.clipboard?.writeText) {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      // Fall through to the selected-text copy path.
    }
  }

  try {
    return document.execCommand?.('copy') === true;
  } catch {
    return false;
  }
}

function ensureMapCardCategoryOptions() {
  const select = document.getElementById('mapCardDefaultCategory');
  if (!select || select.options.length) return;
  select.innerHTML = mapCardCategories
    .map(category => `<option value="${escapeHtml(category.id)}">${escapeHtml(category.name)}</option>`)
    .join('');
  select.value = 'standard';
}

async function openCollectionMapCardExportModal() {
  const collectionId = getSelectedCollectionId();
  if (!collectionId) {
    showToast('Choose a collection');
    return;
  }

  ensureMapCardCategoryOptions();
  const response = await fetch(`/api/collections/${encodeURIComponent(collectionId)}/map-cards`);
  if (!response.ok) throw new Error(await response.text());
  mapCardExportData = await response.json();
  mapCardCategoriesDirty = normalizeMapCardExportCategories(true);
  syncMapCardCategoryToggle();

  const modal = document.getElementById('mapCardExportModal');
  if (!modal) return;
  mapCardExportLastFocus = document.activeElement;
  modal.hidden = false;
  renderMapCardExportPreview();
  showToast('Map cards ready');
}

function closeMapCardExportModal() {
  const modal = document.getElementById('mapCardExportModal');
  if (!modal) return;
  modal.hidden = true;
  mapCardExportData = null;
  mapCardCategoriesDirty = false;
  if (mapCardExportLastFocus && typeof mapCardExportLastFocus.focus === 'function') {
    mapCardExportLastFocus.focus();
  }
  mapCardExportLastFocus = null;
}

function applyMapCardDefaultCategory() {
  if (!mapCardExportData?.items?.length) return;
  const category = document.getElementById('mapCardDefaultCategory')?.value || 'standard';
  for (const item of mapCardExportData.items) {
    item.category = category;
  }
  mapCardCategoriesDirty = true;
  renderMapCardExportPreview();
}

function syncMapCardCategoryToggle() {
  const checkbox = document.getElementById('mapCardCategoriesEnabled');
  if (checkbox) {
    checkbox.checked = mapCardCategoriesEnabled;
  }
  const defaultCategory = document.getElementById('mapCardDefaultCategory');
  if (defaultCategory) {
    defaultCategory.disabled = !mapCardCategoriesEnabled;
  }
}

function toggleMapCardCategories() {
  const enabled = Boolean(document.getElementById('mapCardCategoriesEnabled')?.checked);
  writeMapCardCategoriesEnabled(enabled);
  renderMapCardExportPreview();
}

function normalizeMapCardExportCategories(markDirtyForDefaults = false) {
  if (!mapCardExportData?.items?.length) return false;
  const defaultCategory = document.getElementById('mapCardDefaultCategory')?.value || 'standard';
  let foundUnsavedDefault = false;
  for (const item of mapCardExportData.items) {
    const rawCategory = String(item.category || '').trim();
    const normalizedCategory = rawCategory ? getMapCardCategory(rawCategory).id : defaultCategory;
    item.category = normalizedCategory;
    if (markDirtyForDefaults && rawCategory.toLowerCase() !== normalizedCategory) {
      foundUnsavedDefault = true;
    }
  }
  return foundUnsavedDefault;
}

function renderMapCardExportPreview() {
  const preview = document.getElementById('mapCardExportPreview');
  if (!preview) return;
  const items = mapCardExportData?.items || [];
  const collectionName = mapCardExportData?.collectionName || 'Collection';
  syncMapCardCategoryToggle();
  setText('mapCardExportTitle', `${collectionName} Map Cards`);
  setText('mapCardExportCount', items.length ? `${items.length} map card${items.length === 1 ? '' : 's'}` : 'No map cards');
  setText('mapCardCategorySaveStatus', items.length
    ? (mapCardCategoriesDirty ? 'Unsaved categories' : 'Categories saved')
    : '');
  preview.innerHTML = items.length
    ? items.map(renderMapCardPreviewShell).join('')
    : '<div class="emptyState">No maps found in this collection</div>';

  preview.querySelectorAll('[data-map-card-category]').forEach(select => {
    select.addEventListener('change', () => {
      const id = select.getAttribute('data-map-card-category');
      const item = items.find(candidate => candidate.id === id);
      if (!item) return;
      item.category = select.value || 'standard';
      mapCardCategoriesDirty = true;
      renderMapCardExportPreview();
    });
  });
}

function renderMapCardPreviewShell(item, index) {
  const category = getMapCardCategory(item.category);
  const categoriesEnabled = mapCardCategoriesEnabled;
  const nps = formatMapCardNps(item.notesPerSecond);
  const length = formatSeconds(item.lengthSeconds);
  const bpm = formatMapCardBpm(item.beatsPerMinute);
  const key = cleanQueueExportField(item.beatSaverKey) || 'Unknown';
  const song = cleanQueueExportField(item.songName) || 'Untitled map';
  const artist = cleanQueueExportField(item.artist) || 'Unknown artist';
  const mapper = cleanQueueExportField(item.mapAuthor) || 'Unknown mapper';
  const difficulty = cleanQueueExportField(item.difficulty) || '-';
  return `
    <section class="mapCardPreviewShell">
      <article class="mapCardPreviewCard${categoriesEnabled ? '' : ' noMapCardCategory'}" style="--category-color: ${escapeHtml(category.color)}">
        <div class="mapCardPreviewCover" aria-hidden="true">
          ${item.coverArtUrl ? `<img src="${escapeHtml(item.coverArtUrl)}" alt="" onerror="this.remove()">` : ''}
          <span>${escapeHtml(initials(item.songName || item.beatSaverKey || 'Map'))}</span>
        </div>
        <div class="mapCardPreviewContent">
          <h3 title="${escapeHtml(song)}">${escapeHtml(song)}</h3>
          <p title="${escapeHtml(artist)}">${escapeHtml(artist)}</p>
          <div class="mapCardPreviewMapper" title="Mapped by ${escapeHtml(mapper)}">Mapped by ${escapeHtml(mapper)}</div>
          <div class="mapCardPreviewMetricLine">
            <span class="mapCardMetricGroup">${mapCardMetricSvg('length')}<span>Length: ${escapeHtml(length)}</span></span>
            <span class="mapCardMetricGroup">${mapCardMetricSvg('bpm')}<span>BPM: ${escapeHtml(bpm)}</span></span>
          </div>
          <div class="mapCardPreviewMetricLine">
            <span class="mapCardMetricGroup">${mapCardMetricSvg('difficulty')}<span>Difficulty: ${escapeHtml(difficulty)}</span></span>
            <span class="mapCardMetricGroup">${mapCardMetricSvg('nps')}<span>NPS: ${escapeHtml(nps)}</span></span>
          </div>
        </div>
        <div class="mapCardPreviewSide">
          <div class="mapCardPreviewKey" title="${escapeHtml(key)}">${escapeHtml(key)}</div>
          ${categoriesEnabled ? renderMapCardCategoryPill(category) : ''}
        </div>
      </article>
      <div class="mapCardPreviewControls">
        <label>Category
          <select data-map-card-category="${escapeHtml(item.id)}"${categoriesEnabled ? '' : ' disabled'}>
            ${renderMapCardCategoryOptions(item.category)}
          </select>
        </label>
        <span>${escapeHtml(item.metadataStatus || 'Partial')} | ${escapeHtml(item.metadataDetail || 'Metadata ready')}</span>
      </div>
    </section>
  `;
}

function mapCardMetricSvg(iconId) {
  const icon = mapCardMetricIcons[iconId] || mapCardMetricIcons.nps;
  return `
    <svg aria-hidden="true" width="1em" height="1em" viewBox="0 0 ${icon.width} ${icon.height}" focusable="false">
      ${icon.paths.map(path => `<path d="${escapeHtml(path)}" fill="none" stroke="currentColor" stroke-width="2.3" stroke-linecap="round" stroke-linejoin="round"></path>`).join('')}
    </svg>
  `;
}

function renderMapCardCategoryOptions(selectedCategory) {
  const selected = getMapCardCategory(selectedCategory).id;
  return mapCardCategories.map(category =>
    `<option value="${escapeHtml(category.id)}"${category.id === selected ? ' selected' : ''}>${escapeHtml(category.name)}</option>`
  ).join('');
}

function renderMapCardCategoryPill(category) {
  return `
    <span class="mapCardCategoryPill" style="--category-color: ${escapeHtml(category.color)}">
      <span class="mapCardCategoryInner">
        ${mapCardCategorySvg(category)}
        <span>${escapeHtml(category.name)}</span>
      </span>
    </span>
  `;
}

function mapCardCategorySvg(category) {
  const iconData = mapCardCategoryIcons[category.icon] || mapCardCategoryIcons.standard;
  return `
    <svg aria-hidden="true" width="1em" height="1em" viewBox="0 0 ${iconData.width} ${iconData.height}" focusable="false">
      <path fill-rule="evenodd" clip-rule="evenodd" d="${escapeHtml(iconData.path)}" fill="currentColor"></path>
    </svg>
  `;
}

function getMapCardCategory(categoryId) {
  const normalized = String(categoryId || '').trim().toLowerCase();
  return mapCardCategories.find(category => category.id === normalized) || mapCardCategories[0];
}

async function downloadMapCards() {
  const items = mapCardExportData?.items || [];
  if (!items.length) {
    showToast('No map cards to download');
    return;
  }

  const folderName = createMapCardFolderName(mapCardExportData?.collectionName);
  const directoryHandle = await requestMapCardExportDirectory(folderName);
  if (directoryHandle === false) {
    showToast('Map card export canceled');
    return;
  }

  if (mapCardCategoriesDirty) {
    await saveMapCardCategories({ silent: true });
  }

  const usedNames = new Set();
  const includeCategory = mapCardCategoriesEnabled;
  const gridFileName = createMapCardGridFileName(mapCardExportData?.collectionName);
  if (directoryHandle) {
    for (const item of items) {
      const fileName = createUniqueMapCardFileName(item, usedNames);
      const blob = await createMapCardPngBlob(item, { includeCategory });
      await writeBlobToDirectory(directoryHandle, fileName, blob);
    }
    const gridBlob = await createMapCardGridPngBlob(items, { includeCategory });
    await writeBlobToDirectory(directoryHandle, gridFileName, gridBlob);
    showToast(`Saved ${items.length} map card${items.length === 1 ? '' : 's'} and grid to ${folderName}`);
    return;
  }

  const zipBlob = await createMapCardsZipBlob(items, folderName, { includeCategory, usedNames, includeGrid: true, gridFileName });
  downloadBlob(zipBlob, `${folderName}.zip`);
  showToast(`Downloaded ${folderName}.zip with ${items.length} map card${items.length === 1 ? '' : 's'} and grid`);
}

async function downloadMapCardGrid() {
  const items = mapCardExportData?.items || [];
  if (!items.length) {
    showToast('No map cards to export');
    return;
  }

  if (mapCardCategoriesDirty) {
    await saveMapCardCategories({ silent: true });
  }

  const includeCategory = mapCardCategoriesEnabled;
  const blob = await createMapCardGridPngBlob(items, { includeCategory });
  downloadBlob(blob, createMapCardGridFileName(mapCardExportData?.collectionName));
  showToast(`Downloaded grid with ${items.length} map card${items.length === 1 ? '' : 's'}`);
}

async function saveMapCardCategories({ silent = false } = {}) {
  const collectionId = mapCardExportData?.collectionId;
  const items = mapCardExportData?.items || [];
  if (!collectionId || !items.length) {
    showToast('No map card categories to save');
    return false;
  }

  const response = await fetch(`/api/collections/${encodeURIComponent(collectionId)}/map-cards/categories`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      items: items.map(item => ({
        itemId: item.id,
        category: getMapCardCategory(item.category).id
      }))
    })
  });
  if (!response.ok) throw new Error(await response.text());
  mapCardExportData = await response.json();
  normalizeMapCardExportCategories(false);
  mapCardCategoriesDirty = false;
  renderMapCardExportPreview();
  if (!silent) {
    showToast('Categories saved to collection');
  }

  return true;
}

async function createMapCardPngBlob(item, options = {}) {
  const canvas = await createMapCardCanvasForItem(item, options);
  return canvasToPngBlob(canvas);
}

async function createMapCardCanvasForItem(item, options = {}) {
  const image = await loadMapCardImage(item.coverArtUrl);
  try {
    return createMapCardCanvas(item, image, options);
  } catch {
    return createMapCardCanvas(item, null, options);
  }
}

function createMapCardCanvas(item, image, options = {}) {
  const canvas = document.createElement('canvas');
  canvas.width = mapCardExportSize.width;
  canvas.height = mapCardExportSize.height;
  const context = canvas.getContext('2d');
  drawMapCardCanvas(context, item, image, options);
  return canvas;
}

function canvasToPngBlob(canvas) {
  return new Promise((resolve, reject) => {
    try {
      canvas.toBlob(blob => {
        if (blob) resolve(blob);
        else reject(new Error('Map card image export failed.'));
      }, 'image/png');
    } catch (error) {
      reject(error);
    }
  });
}

async function createMapCardGridPngBlob(items, options = {}) {
  const cards = Array.isArray(items) ? items : [];
  if (!cards.length) {
    throw new Error('No map cards to export.');
  }

  const columnsPerRow = mapCardGridLayout.columns;
  const visibleColumns = Math.min(columnsPerRow, cards.length);
  const rowCount = Math.ceil(cards.length / columnsPerRow);
  const cardWidth = mapCardExportSize.width;
  const cardHeight = mapCardExportSize.height;
  const gapX = mapCardGridLayout.columnGap;
  const gapY = mapCardGridLayout.rowGap;
  const canvas = document.createElement('canvas');
  canvas.width = visibleColumns * cardWidth + Math.max(0, visibleColumns - 1) * gapX;
  canvas.height = rowCount * cardHeight + Math.max(0, rowCount - 1) * gapY;
  const context = canvas.getContext('2d');
  context.clearRect(0, 0, canvas.width, canvas.height);

  for (let index = 0; index < cards.length; index++) {
    const row = Math.floor(index / columnsPerRow);
    const column = index % columnsPerRow;
    const rowItemCount = Math.min(columnsPerRow, cards.length - row * columnsPerRow);
    const rowWidth = rowItemCount * cardWidth + Math.max(0, rowItemCount - 1) * gapX;
    const rowStartX = Math.max(0, (canvas.width - rowWidth) / 2);
    const x = rowStartX + column * (cardWidth + gapX);
    const y = row * (cardHeight + gapY);
    const cardCanvas = await createMapCardCanvasForItem(cards[index], options);
    context.drawImage(cardCanvas, x, y);
  }

  return canvasToPngBlob(canvas);
}

function drawMapCardCanvas(ctx, item, image, options = {}) {
  drawFixedMapCardCanvas(ctx, item, image, options);
  return;

  const category = getMapCardCategory(item.category);
  const cardWidth = 480;
  const cardHeight = 131;
  const contentX = 130;
  const key = cleanQueueExportField(item.beatSaverKey) || 'Unknown';
  const mapper = cleanQueueExportField(item.mapAuthor) || 'Unknown mapper';
  const categoryMetrics = measureCanvasCategoryPill(ctx, category, 12);

  ctx.clearRect(0, 0, cardWidth, cardHeight);
  ctx.fillStyle = '#fff';
  ctx.fillRect(0, 0, cardWidth, cardHeight);
  drawMapCardCover(ctx, image, item, 16, 16, 100, 0, 32);

  ctx.textAlign = 'start';
  ctx.textBaseline = 'top';
  ctx.fillStyle = '#000';
  ctx.font = `800 20px ${mapCardFontFamily}`;
  drawFittedCanvasText(ctx, cleanQueueExportField(item.songName) || 'Untitled map', contentX, 17, 280);

  ctx.font = `500 14px ${mapCardFontFamily}`;
  drawFittedCanvasText(ctx, cleanQueueExportField(item.artist) || 'Unknown artist', contentX, 46, 300);

  ctx.fillStyle = '#6f6f6f';
  ctx.font = `700 13px ${mapCardFontFamily}`;
  drawFittedCanvasText(ctx, `Mapped by ${mapper}`, contentX, 70, 300);

  ctx.font = `500 13px ${mapCardFontFamily}`;
  drawFittedCanvasText(
    ctx,
    `Length: ${formatSeconds(item.lengthSeconds)} BPM: ${formatMapCardBpm(item.beatsPerMinute)}`,
    contentX,
    86,
    250);
  drawFittedCanvasText(ctx, `Difficulty: ${cleanQueueExportField(item.difficulty) || '-'} NPS: ${formatMapCardNps(item.notesPerSecond)}`, contentX, 102, 250);

  ctx.fillStyle = '#000';
  ctx.font = `500 13px ${mapCardFontFamily}`;
  ctx.textAlign = 'right';
  ctx.textBaseline = 'top';
  drawFittedCanvasText(ctx, key, 464, 15, 82);
  ctx.textAlign = 'start';

  drawCanvasCategoryPill(ctx, category, cardWidth - 16 - categoryMetrics.width, 92, 12);
}

function drawFixedMapCardCanvas(ctx, item, image, options = {}) {
  const category = getMapCardCategory(item.category);
  const includeCategory = options.includeCategory !== false;
  const cardWidth = mapCardExportSize.width;
  const cardHeight = mapCardExportSize.height;
  const frame = { x: 1.5, y: 1.5, width: cardWidth - 3, height: cardHeight - 3, radius: 12 };
  const cover = { x: 18, y: 15, size: 120, radius: 8 };
  const contentX = 154;
  const contentRight = 390;
  const sideLeft = 408;
  const sideRight = 487;
  const sideCenter = (sideLeft + sideRight) / 2;
  const key = cleanQueueExportField(item.beatSaverKey) || 'Unknown';
  const mapper = cleanQueueExportField(item.mapAuthor) || 'Unknown mapper';
  const categoryMetrics = measureCanvasCategoryPill(ctx, category, 13.5, 78);

  ctx.clearRect(0, 0, cardWidth, cardHeight);
  ctx.save();
  ctx.shadowColor = 'rgba(15, 23, 42, 0.17)';
  ctx.shadowBlur = 12;
  ctx.shadowOffsetY = 5;
  ctx.fillStyle = '#ffffff';
  roundRect(ctx, frame.x, frame.y, frame.width, frame.height, frame.radius, true, false);
  ctx.restore();
  ctx.strokeStyle = '#b8bec6';
  ctx.lineWidth = 1;
  roundRect(ctx, frame.x, frame.y, frame.width, frame.height, frame.radius, false, true);

  drawMapCardCover(ctx, image, item, cover.x, cover.y, cover.size, cover.radius, 32);

  ctx.textAlign = 'start';
  ctx.textBaseline = 'top';
  ctx.fillStyle = '#030303';
  ctx.font = `650 16px ${mapCardFontFamily}`;
  drawFittedCanvasText(ctx, cleanQueueExportField(item.songName) || 'Untitled map', contentX, 18, contentRight - contentX);

  ctx.fillStyle = '#000000';
  ctx.font = `500 15px ${mapCardFontFamily}`;
  drawFittedCanvasText(ctx, cleanQueueExportField(item.artist) || 'Unknown artist', contentX, 46, contentRight - contentX);

  ctx.fillStyle = '#4f5967';
  ctx.font = `500 13.5px ${mapCardFontFamily}`;
  drawFittedCanvasText(ctx, `Mapped by ${mapper}`, contentX, 68, contentRight - contentX);

  let metricX = drawCanvasMetricGroup(
    ctx,
    'length',
    `Length: ${formatSeconds(item.lengthSeconds)}`,
    contentX,
    102,
    105);
  metricX += 14;
  drawCanvasMetricGroup(ctx, 'bpm', `BPM: ${formatMapCardBpm(item.beatsPerMinute)}`, metricX, 102, contentRight - metricX);
  metricX = drawCanvasMetricGroup(ctx, 'difficulty', `Difficulty: ${cleanQueueExportField(item.difficulty) || '-'}`, contentX, 128, 134);
  metricX += 14;
  drawCanvasMetricGroup(ctx, 'nps', `NPS: ${formatMapCardNps(item.notesPerSecond)}`, metricX, 128, contentRight - metricX);

  ctx.fillStyle = '#050505';
  ctx.font = `650 16px ${mapCardFontFamily}`;
  ctx.textAlign = 'right';
  ctx.textBaseline = 'top';
  drawFittedCanvasText(ctx, key, sideRight, 17, sideRight - sideLeft);
  ctx.textAlign = 'start';

  if (includeCategory) {
    drawCanvasCategoryPill(ctx, category, sideRight - categoryMetrics.width, cardHeight - 15 - categoryMetrics.height, 13.5, 78);
  }
}

function drawMapCardCover(ctx, image, item, x, y, size, radius, initialsSize = 28) {
  roundRect(ctx, x, y, size, size, radius, false, false);
  ctx.save();
  ctx.clip();
  if (image) {
    drawImageCover(ctx, image, x, y, size, size);
  } else {
    ctx.fillStyle = '#f5f5f5';
    ctx.fillRect(x, y, size, size);
    ctx.fillStyle = '#bfc8d0';
    ctx.font = `800 ${initialsSize}px ${mapCardFontFamily}`;
    ctx.textAlign = 'center';
    ctx.textBaseline = 'middle';
    ctx.fillText(initials(item.songName || item.beatSaverKey || 'Map'), x + size / 2, y + size / 2);
    ctx.textAlign = 'start';
  }
  ctx.restore();
}

function measureCanvasCategoryPill(ctx, category, fontSize = 14.4, maxWidth = Infinity) {
  ctx.font = `700 ${fontSize}px ${mapCardFontFamily}`;
  const textWidth = ctx.measureText(category.name).width;
  const desiredWidth = Math.ceil(textWidth + fontSize + 28);
  return {
    width: Math.min(desiredWidth, maxWidth),
    height: Math.ceil(fontSize + 20),
    iconSize: Math.ceil(fontSize + 1)
  };
}

function drawCanvasCategoryPill(ctx, category, x, y, fontSize = 14.4, maxWidth = Infinity) {
  const metrics = measureCanvasCategoryPill(ctx, category, fontSize, maxWidth);
  const { width, height, iconSize } = metrics;
  ctx.fillStyle = category.color;
  roundRect(ctx, x, y, width, height, 8, true, false);
  ctx.strokeStyle = category.color;
  ctx.lineWidth = 1;
  roundRect(ctx, x + 0.5, y + 0.5, width - 1, height - 1, 8, false, true);
  ctx.fillStyle = '#000';
  const icon = mapCardCategoryIcons[category.icon] || mapCardCategoryIcons.standard;
  const iconX = x + 9;
  drawCanvasCategoryIcon(ctx, icon, iconX, y + (height - iconSize) / 2, iconSize);
  ctx.textBaseline = 'middle';
  ctx.font = `700 ${fontSize}px ${mapCardFontFamily}`;
  drawFittedCanvasText(ctx, category.name, iconX + iconSize + 7, y + height / 2, width - iconSize - 24);
}

function drawCanvasCategoryIcon(ctx, icon, x, y, size) {
  const scale = size / Math.max(icon.width, icon.height);
  const path = new Path2D(icon.path);
  ctx.save();
  ctx.translate(x, y);
  ctx.scale(scale, scale);
  ctx.fill(path);
  ctx.restore();
}

function drawCanvasMetricGroup(ctx, iconId, text, x, centerY, maxWidth) {
  const iconSize = 15;
  const gap = 6;
  const icon = mapCardMetricIcons[iconId] || mapCardMetricIcons.nps;
  const textX = x + iconSize + gap;
  const textMaxWidth = Math.max(0, maxWidth - iconSize - gap);
  ctx.strokeStyle = '#4f5967';
  drawCanvasMetricIcon(ctx, icon, x, centerY - iconSize / 2, iconSize);
  ctx.fillStyle = '#4f5967';
  ctx.font = `600 13.5px ${mapCardFontFamily}`;
  ctx.textAlign = 'start';
  ctx.textBaseline = 'middle';
  const fitted = fitCanvasText(ctx, text, textMaxWidth);
  ctx.fillText(fitted, textX, centerY);
  return textX + ctx.measureText(fitted).width;
}

function drawCanvasMetricIcon(ctx, icon, x, y, size) {
  const scale = size / Math.max(icon.width, icon.height);
  ctx.save();
  ctx.translate(x, y);
  ctx.scale(scale, scale);
  ctx.lineWidth = 2.3;
  ctx.lineCap = 'round';
  ctx.lineJoin = 'round';
  for (const pathData of icon.paths) {
    ctx.stroke(new Path2D(pathData));
  }
  ctx.restore();
}

function drawStatBlock(ctx, x, y, width, height, label, value) {
  ctx.fillStyle = 'rgba(255,255,255,0.055)';
  roundRect(ctx, x, y, width, height, 12, true, false);
  ctx.strokeStyle = 'rgba(255,255,255,0.1)';
  ctx.lineWidth = 1;
  roundRect(ctx, x, y, width, height, 12, false, true);
  ctx.fillStyle = '#8b9aa8';
  ctx.font = `700 13px ${mapCardFontFamily}`;
  ctx.fillText(label.toUpperCase(), x + 14, y + 22);
  ctx.fillStyle = '#f1f6fb';
  ctx.font = `800 24px ${mapCardFontFamily}`;
  drawFittedCanvasText(ctx, String(value || '-'), x + 14, y + 49, width - 28);
}

function drawWrappedCanvasText(ctx, text, x, y, maxWidth, lineHeight, maxLines) {
  const words = String(text || '').split(/\s+/).filter(Boolean);
  const lines = [];
  let current = '';
  let consumedWords = 0;
  for (const word of words) {
    consumedWords++;
    const next = current ? `${current} ${word}` : word;
    if (ctx.measureText(next).width <= maxWidth || !current) {
      current = next;
    } else {
      lines.push(current);
      current = word;
    }
    if (lines.length === maxLines) break;
  }
  if (current && lines.length < maxLines) lines.push(current);
  if (consumedWords < words.length && lines.length === maxLines) {
    while (ctx.measureText(`${lines[maxLines - 1]}...`).width > maxWidth && lines[maxLines - 1].length > 1) {
      lines[maxLines - 1] = lines[maxLines - 1].slice(0, -1);
    }
    lines[maxLines - 1] += '...';
  }
  lines.forEach((line, index) => ctx.fillText(line, x, y + index * lineHeight));
}

function drawFittedCanvasText(ctx, text, x, y, maxWidth) {
  ctx.fillText(fitCanvasText(ctx, text, maxWidth), x, y);
}

function fitCanvasText(ctx, text, maxWidth) {
  let output = String(text || '');
  while (ctx.measureText(output).width > maxWidth && output.length > 1) {
    output = output.slice(0, -1);
  }
  if (output !== String(text || '') && output.length > 3) output = `${output.slice(0, -3)}...`;
  return output;
}

function drawImageCover(ctx, image, x, y, width, height) {
  const imageRatio = image.width / Math.max(1, image.height);
  const targetRatio = width / height;
  let sourceWidth = image.width;
  let sourceHeight = image.height;
  let sourceX = 0;
  let sourceY = 0;
  if (imageRatio > targetRatio) {
    sourceWidth = image.height * targetRatio;
    sourceX = (image.width - sourceWidth) / 2;
  } else {
    sourceHeight = image.width / targetRatio;
    sourceY = (image.height - sourceHeight) / 2;
  }
  ctx.drawImage(image, sourceX, sourceY, sourceWidth, sourceHeight, x, y, width, height);
}

function roundRect(ctx, x, y, width, height, radius, fill, stroke) {
  const r = Math.min(radius, width / 2, height / 2);
  ctx.beginPath();
  ctx.moveTo(x + r, y);
  ctx.lineTo(x + width - r, y);
  ctx.quadraticCurveTo(x + width, y, x + width, y + r);
  ctx.lineTo(x + width, y + height - r);
  ctx.quadraticCurveTo(x + width, y + height, x + width - r, y + height);
  ctx.lineTo(x + r, y + height);
  ctx.quadraticCurveTo(x, y + height, x, y + height - r);
  ctx.lineTo(x, y + r);
  ctx.quadraticCurveTo(x, y, x + r, y);
  ctx.closePath();
  if (fill) ctx.fill();
  if (stroke) ctx.stroke();
}

function formatMapCardNps(value) {
  const number = Number(value);
  if (!Number.isFinite(number) || number <= 0) return '-';
  return number.toFixed(2);
}

function formatMapCardBpm(value) {
  const number = Number(value);
  if (!Number.isFinite(number) || number <= 0) return '-';
  const rounded = Math.round(number);
  return Math.abs(number - rounded) < 0.05 ? String(rounded) : number.toFixed(1);
}

function loadMapCardImage(src) {
  const url = String(src || '').trim();
  if (!url) return Promise.resolve(null);
  return new Promise(resolve => {
    const image = new Image();
    if (/^https?:\/\//i.test(url)) image.crossOrigin = 'anonymous';
    image.onload = () => resolve(image);
    image.onerror = () => resolve(null);
    image.src = url;
  });
}

function createMapCardFileName(item) {
  const base = [
    String(item.sequenceNumber || '').padStart(2, '0'),
    cleanQueueExportField(item.songName) || 'map-card',
    cleanQueueExportField(item.difficulty)
  ].filter(Boolean).join(' - ');
  return `${sanitizeDownloadFileName(base)}.png`;
}

function createUniqueMapCardFileName(item, usedNames) {
  const fileName = createMapCardFileName(item);
  const dotIndex = fileName.lastIndexOf('.');
  const stem = dotIndex > 0 ? fileName.slice(0, dotIndex) : fileName;
  const extension = dotIndex > 0 ? fileName.slice(dotIndex) : '';
  let candidate = fileName;
  let index = 2;
  while (usedNames.has(candidate.toLowerCase())) {
    candidate = `${stem} (${index})${extension}`;
    index++;
  }
  usedNames.add(candidate.toLowerCase());
  return candidate;
}

function createMapCardFolderName(collectionName) {
  const baseName = sanitizeDownloadFileName(cleanQueueExportField(collectionName) || 'Map Cards');
  return sanitizeDownloadFileName(`${baseName} Map Cards`);
}

function createMapCardGridFileName(collectionName) {
  const baseName = sanitizeDownloadFileName(cleanQueueExportField(collectionName) || 'Map Cards');
  return `${sanitizeDownloadFileName(`${baseName} Map Card Grid`)}.png`;
}

async function requestMapCardExportDirectory(folderName) {
  if (typeof window.showDirectoryPicker !== 'function') return null;
  try {
    const rootHandle = await window.showDirectoryPicker({
      id: 'map-card-export',
      mode: 'readwrite',
      startIn: 'downloads'
    });
    return await rootHandle.getDirectoryHandle(folderName, { create: true });
  } catch (error) {
    if (error?.name === 'AbortError') return false;
    throw error;
  }
}

async function writeBlobToDirectory(directoryHandle, fileName, blob) {
  const fileHandle = await directoryHandle.getFileHandle(fileName, { create: true });
  const writable = await fileHandle.createWritable();
  try {
    await writable.write(blob);
  } finally {
    await writable.close();
  }
}

async function createMapCardsZipBlob(items, folderName, options = {}) {
  const usedNames = options.usedNames || new Set();
  const entries = [];
  if (options.includeGrid) {
    const gridBlob = await createMapCardGridPngBlob(items, { includeCategory: options.includeCategory });
    entries.push({
      name: `${folderName}/${options.gridFileName || createMapCardGridFileName(folderName)}`,
      data: new Uint8Array(await gridBlob.arrayBuffer())
    });
  }

  for (const item of items) {
    const fileName = createUniqueMapCardFileName(item, usedNames);
    const blob = await createMapCardPngBlob(item, { includeCategory: options.includeCategory });
    entries.push({
      name: `${folderName}/${fileName}`,
      data: new Uint8Array(await blob.arrayBuffer())
    });
  }

  return createZipBlob(entries);
}

function createZipBlob(entries) {
  const fileParts = [];
  const centralParts = [];
  let offset = 0;
  for (const entry of entries) {
    const nameBytes = new TextEncoder().encode(entry.name.replace(/\\/g, '/'));
    const data = entry.data instanceof Uint8Array ? entry.data : new Uint8Array(entry.data || []);
    const crc = crc32(data);
    const { date, time } = zipDateTime(new Date());
    const localHeader = new Uint8Array(30 + nameBytes.length);
    const localView = new DataView(localHeader.buffer);
    localView.setUint32(0, 0x04034b50, true);
    localView.setUint16(4, 20, true);
    localView.setUint16(6, 0x0800, true);
    localView.setUint16(8, 0, true);
    localView.setUint16(10, time, true);
    localView.setUint16(12, date, true);
    localView.setUint32(14, crc, true);
    localView.setUint32(18, data.length, true);
    localView.setUint32(22, data.length, true);
    localView.setUint16(26, nameBytes.length, true);
    localHeader.set(nameBytes, 30);
    fileParts.push(localHeader, data);

    const centralHeader = new Uint8Array(46 + nameBytes.length);
    const centralView = new DataView(centralHeader.buffer);
    centralView.setUint32(0, 0x02014b50, true);
    centralView.setUint16(4, 20, true);
    centralView.setUint16(6, 20, true);
    centralView.setUint16(8, 0x0800, true);
    centralView.setUint16(10, 0, true);
    centralView.setUint16(12, time, true);
    centralView.setUint16(14, date, true);
    centralView.setUint32(16, crc, true);
    centralView.setUint32(20, data.length, true);
    centralView.setUint32(24, data.length, true);
    centralView.setUint16(28, nameBytes.length, true);
    centralView.setUint32(42, offset, true);
    centralHeader.set(nameBytes, 46);
    centralParts.push(centralHeader);
    offset += localHeader.length + data.length;
  }

  const centralSize = centralParts.reduce((total, part) => total + part.length, 0);
  const endHeader = new Uint8Array(22);
  const endView = new DataView(endHeader.buffer);
  endView.setUint32(0, 0x06054b50, true);
  endView.setUint16(8, entries.length, true);
  endView.setUint16(10, entries.length, true);
  endView.setUint32(12, centralSize, true);
  endView.setUint32(16, offset, true);
  return new Blob([...fileParts, ...centralParts, endHeader], { type: 'application/zip' });
}

function zipDateTime(value) {
  const year = Math.max(1980, value.getFullYear());
  return {
    time: (value.getHours() << 11) | (value.getMinutes() << 5) | Math.floor(value.getSeconds() / 2),
    date: ((year - 1980) << 9) | ((value.getMonth() + 1) << 5) | value.getDate()
  };
}

function crc32(data) {
  let crc = 0xffffffff;
  for (let index = 0; index < data.length; index++) {
    crc = (crc >>> 8) ^ crc32Table[(crc ^ data[index]) & 0xff];
  }
  return (crc ^ 0xffffffff) >>> 0;
}

const crc32Table = (() => {
  const table = new Uint32Array(256);
  for (let index = 0; index < table.length; index++) {
    let value = index;
    for (let bit = 0; bit < 8; bit++) {
      value = (value & 1) ? (0xedb88320 ^ (value >>> 1)) : (value >>> 1);
    }
    table[index] = value >>> 0;
  }
  return table;
})();

function sanitizeDownloadFileName(value) {
  return String(value || 'map-card')
    .replace(/[<>:"/\\|?*\u0000-\u001F]+/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
    .slice(0, 120) || 'map-card';
}

function downloadBlob(blob, fileName) {
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  window.setTimeout(() => URL.revokeObjectURL(url), 3000);
}

function delay(milliseconds) {
  return new Promise(resolve => window.setTimeout(resolve, milliseconds));
}

async function openRecordedFile(id) {
  if (!id) return;
  await runAction(async () => {
    const response = await fetch(queueUrl(id, 'recording/open'), { method: 'POST' });
    if (!response.ok) throw new Error(await response.text());

    const result = await response.json();
    showToast(result.status || 'Recording opened in File Explorer');
  });
}

function queueUrl(id, action) {
  return `/api/queue/${encodeURIComponent(id)}/${action}`;
}

function matchesQueueSearch(item) {
  const query = normalizedQueueSearch();
  if (!query) return true;

  const fields = [
    item.sequenceNumber,
    item.songName,
    item.mapper,
    formatPlayerName(item.playerName),
    item.difficulty,
    item.fileName,
    item.path,
    item.outputPath,
    item.error,
    item.levelHash,
    item.mapStatus,
    item.mapStatusDetail,
    item.mapInstallPath,
    item.syncStatus
  ];

  return fields.some(value => String(value ?? '').toLowerCase().includes(query));
}

function normalizedQueueSearch() {
  return queueSearchText.trim().toLowerCase();
}

function icon(name) {
  const symbols = {
    up: '&#9650;',
    down: '&#9660;',
    edit: '&#9998;',
    open: '&#9654;',
    download: '&#8595;',
    upload: '&#8593;',
    refresh: '&#8635;',
    trash: '&#215;',
    save: '&#10003;',
    close: '&#215;',
    list: '&#9776;',
    wave: '&#8767;'
  };

  return `<span class="nativeIcon" aria-hidden="true">${symbols[name] || ''}</span>`;
}

function setValue(id, value) {
  const element = document.getElementById(id);
  if (!element) return;
  element.value = value ?? '';
}

function setDisabled(id, disabled) {
  const element = document.getElementById(id);
  if (!element) return;
  element.disabled = Boolean(disabled);
}

function renderMonitorOptions() {
  const select = document.getElementById('monitorIndex');
  if (!select) return;

  const savedValue = String(state?.settings?.monitorIndex ?? 1);
  const selectedValue = select.value || savedValue;
  const displays = Array.isArray(displayInfo?.displays)
    ? displayInfo.displays.filter(display => Number.isFinite(Number(display.index)))
    : [];
  const options = displays.length
    ? displays
      .slice()
      .sort((first, second) => Number(first.index) - Number(second.index))
      .map(display => ({
        value: String(display.index),
        label: buildDetectedMonitorLabel(display)
      }))
    : [{
      value: selectedValue,
      label: `Saved monitor ${selectedValue} (display detection unavailable)`
    }];

  select.innerHTML = '';
  for (const option of options) {
    const element = document.createElement('option');
    element.value = option.value;
    element.textContent = option.label;
    select.appendChild(element);
  }

  select.value = options.some(option => option.value === selectedValue)
    ? selectedValue
    : options[0]?.value ?? '';
  renderSetupMonitorGraphic(state ? buildCurrentSettingsPreview() : { monitorIndex: Number(select.value || selectedValue) });
}

function updateCaptureEngineWarning() {
  const warning = document.getElementById('captureEngineWarning');
  if (!warning) return;

  warning.hidden = getText('captureEngine') !== 'WindowsGraphicsCapture';
}

function setText(id, value) {
  const element = document.getElementById(id);
  if (!element) return;
  element.textContent = value ?? '';
}

function getNumber(id) {
  return Number(document.getElementById(id)?.value);
}

function getNumberOrSetting(id, settingName, fallback = 0) {
  const element = document.getElementById(id);
  if (element) return Number(element.value);
  const value = state?.settings?.gamePresentation?.[settingName] ?? state?.settings?.[settingName] ?? fallback;
  return Number(value);
}

function buildDetectedMonitorLabel(display) {
  if (display?.label) return trimMonitorIndexSuffix(display.label);

  const index = Number(display?.index);
  const monitorNumber = Number(display?.monitorNumber);
  const labelParts = [
    Number.isFinite(monitorNumber) && monitorNumber > 0
      ? `Monitor ${monitorNumber}`
      : 'Monitor'
  ];

  if (display?.friendlyName) labelParts.push(display.friendlyName);
  if (display?.width && display?.height) labelParts.push(`${display.width} x ${display.height}`);
  if (display?.isPrimary) labelParts.push('Primary');

  return labelParts.join(' - ');
}

function trimMonitorIndexSuffix(label) {
  return String(label ?? '').replace(/\s+-\s+index\s+\d+\s*$/i, '').trim();
}

function clampManagedInstanceCount(value) {
  const count = Math.trunc(Number(value));
  if (!Number.isFinite(count)) return minManagedInstanceCount;
  return Math.min(maxManagedInstanceCount, Math.max(minManagedInstanceCount, count));
}

function clampPositiveInteger(value, minimum, maximum) {
  const count = Math.trunc(Number(value));
  if (!Number.isFinite(count)) return minimum;
  return Math.min(maximum, Math.max(minimum, count));
}

function getCreatedInstanceCount(settings = {}) {
  const desiredCount = getManagedInstanceInventoryCount(settings);
  const provision = state?.instanceProvision || {};
  const reportCount = Number(provision.createdInstanceCount);
  if (Number.isFinite(reportCount) && reportCount > 0) {
    return Math.min(desiredCount, reportCount);
  }

  return Math.min(
    desiredCount,
    (state?.instances || []).filter(instance =>
      Number(instance.index) < desiredCount &&
      instance.launchDirectoryReady).length);
}

function getManagedInstanceInventoryCount(settings = {}) {
  const settingCount = Number(state?.settings?.instanceCount);
  const previewCount = Number(settings.instanceCount);
  const fieldCount = Number(document.getElementById('instanceCount')?.value);
  const provisionCount = Number(state?.instanceProvision?.desiredInstanceCount);
  const recordCount = Math.max(
    0,
    ...(state?.instances || []).map(instance => Number(instance.index) + 1).filter(Number.isFinite));
  return clampManagedInstanceCount(Math.max(
    minManagedInstanceCount,
    Number.isFinite(settingCount) ? settingCount : 0,
    Number.isFinite(previewCount) ? previewCount : 0,
    Number.isFinite(fieldCount) ? fieldCount : 0,
    Number.isFinite(provisionCount) ? provisionCount : 0,
    recordCount));
}

function getText(id) {
  return document.getElementById(id).value;
}

function getSetupSourcePathValue(settings = {}) {
  const fieldValue = document.getElementById('setupSourceBeatSaberPath')?.value?.trim() || '';
  return fieldValue ||
    String(settings.sourceBeatSaberPath || '').trim() ||
    setupSourcePathInfo?.effectiveSourceBeatSaberPath ||
    setupSourcePathInfo?.detectedSourceBeatSaberPath ||
    '';
}

function sameClientPath(left, right) {
  const normalize = value => String(value || '')
    .trim()
    .replace(/[\\/]+$/, '')
    .toLowerCase();
  const normalizedLeft = normalize(left);
  const normalizedRight = normalize(right);
  return normalizedLeft !== '' && normalizedLeft === normalizedRight;
}

function getTextOrSetting(id, settingName) {
  const element = document.getElementById(id);
  return element ? element.value : state.settings[settingName];
}

function getCheckedOrSetting(id, settingName) {
  const element = document.getElementById(id);
  return element ? element.checked : state.settings[settingName] !== false;
}

function getGameCheckedOrSetting(id, settingName, fallback = false) {
  const element = document.getElementById(id);
  if (element) return element.checked;
  return state?.settings?.gamePresentation?.[settingName] ?? fallback;
}

function getLines(id) {
  return getText(id)
    .split(/\r?\n/)
    .map(line => line.trim())
    .filter(Boolean);
}

function setResolutionPreset(width, height) {
  const select = document.getElementById('resolutionPreset');
  const value = `${Number(width) || 0}x${Number(height) || 0}`;
  const hasPreset = Array.from(select.options).some(option => option.value === value);
  select.value = hasPreset ? value : 'custom';
}

function resolveLaunchPreset(settings) {
  const configuredPreset = String(settings?.beatSaberLaunchPreset || '').toLowerCase();
  if (configuredPreset === '720p-monitor-2x2' && launchPresetMatches(settings, launchPresets['720p-monitor-2x2'], '720p-monitor-2x2')) {
    return '720p-monitor-2x2';
  }

  if (configuredPreset === '1440p-monitor-2x2' && launchPresetMatches(settings, launchPresets['1440p-monitor-2x2'], '1440p-monitor-2x2')) {
    return '1440p-monitor-2x2';
  }

  const presetOrder = [
    '5k-monitor-2x2',
    '4k-monitor-2x2',
    '720p-monitor-2x2',
    '1440p-monitor-2x2',
    'single-4k',
    'single-5k',
    'single-1440p',
    'single-1080p',
    'single-720p',
    'windowed-720p',
    'windowed-1080p'
  ];
  return presetOrder.find(presetId => launchPresetMatches(settings, launchPresets[presetId], presetId)) || 'custom';
}

function launchPresetMatches(settings, preset, presetId = '') {
  if (!settings || !preset) return false;

  return Object.entries(preset).every(([fieldId, expected]) => {
    if (fieldId === 'instanceCount' || fieldId === 'maxConcurrentRecordings') return true;

    const actual = settings[fieldId];
    if (typeof expected === 'number') return Number(actual) === expected;
    if (typeof expected === 'boolean') return Boolean(actual) === expected;
    return String(actual ?? '') === String(expected);
  });
}

function applyLaunchPreset(id) {
  const preset = launchPresets[id];
  if (!preset) return;

  const values = {
    ...(launchPresetApplyDefaults[id] || {}),
    ...preset
  };

  for (const [fieldId, value] of Object.entries(values)) {
    if (fieldId === 'instanceCount' || fieldId === 'maxConcurrentRecordings') continue;

    const element = document.getElementById(fieldId);
    if (!element) continue;

    if (element.type === 'checkbox') {
      element.checked = Boolean(value);
    } else {
      setValue(fieldId, value);
    }
  }

  setResolutionPreset(getNumber('captureWidth'), getNumber('captureHeight'));
  updateDisplayScaleAvailability();
  setValue('beatSaberLaunchPreset', id);
}

function markLaunchPresetCustom() {
  if (isRendering) return;
  setValue('beatSaberLaunchPreset', 'custom');
}

function applySetupProfile(id) {
  const profile = setupProfiles[id];
  if (!profile) return;

  if (profile.launchPreset) {
    applyLaunchPreset(profile.launchPreset);
  }

  for (const [fieldId, value] of Object.entries(profile.settings || {})) {
    setFieldValue(fieldId, value);
  }

  pendingSetupEnabledInstanceCount = getSetupProfileEnabledInstanceCount(id);
  updateAudioLevelTargetConstraints();
  settingsDirty = true;
  updateSettingsDirtyBadge();
  renderSetupAssistant();
  showToast('Setup profile applied');
}

function setFieldValue(fieldId, value) {
  const element = document.getElementById(fieldId);
  if (!element) return;

  if (element.type === 'checkbox') {
    element.checked = Boolean(value);
    return;
  }

  setValue(fieldId, value);
}

function renderSetupAssistant() {
  ensureGameSettingsPlacement();

  const panel = document.getElementById('setupAssistant');
  if (!panel || !state) return;

  setupAssistantHidden = false;
  panel.hidden = false;
  const showButton = document.getElementById('showSetupAssistant');
  if (showButton) showButton.hidden = true;

  const settings = buildCurrentSettingsPreview();
  const profileId = resolveSetupProfile(settings);
  const configuredCount = getManagedInstanceInventoryCount(settings);
  const createdCount = getCreatedInstanceCount(settings);
  const missingCount = Math.max(0, configuredCount - createdCount);
  renderFeedPresetList(settings, profileId);
  renderSelectedMonitorSummary(settings);
  renderSetupMonitorGraphic(settings);
  renderSetupInstanceList(settings, configuredCount, createdCount, missingCount);
  renderSetupSourcePath(settings, configuredCount, createdCount, missingCount);

  const summary = document.getElementById('setupAssistantSummary');
  if (summary) {
    summary.textContent = createdCount === 0
      ? `Setup needed | ${formatSetupProfile(profileId)} | ${formatMonitorSummary(settings.monitorIndex)} | ${formatAudioMode(settings)}`
      : `${createdCount}/${configuredCount} created | ${formatSetupProfile(profileId)} | ${formatMonitorSummary(settings.monitorIndex)} | ${formatAudioMode(settings)}`;
  }
}

function renderSetupSourcePath(settings = {}, configuredCount = null, createdCount = null, missingCount = null) {
  const hint = document.getElementById('setupSourceBeatSaberHint');
  const useDetectedButton = document.getElementById('setupUseDetectedSource');
  const setupButton = document.getElementById('setupWizardRunSetup');
  const sourcePath = getSetupSourcePathValue(settings);
  const detectedPath = setupSourcePathInfo?.detectedSourceBeatSaberPath || '';
  const effectivePath = setupSourcePathInfo?.effectiveSourceBeatSaberPath || '';

  if (useDetectedButton) {
    useDetectedButton.disabled = !detectedPath;
    useDetectedButton.title = detectedPath || 'No Steam Beat Saber install detected yet.';
  }

  if (hint) {
    let statusClass = 'missing';
    let text = 'Choose the folder containing Beat Saber.exe.';
    if (sourcePath && sameClientPath(sourcePath, setupSourcePathInfo?.configuredSourceBeatSaberPath) && setupSourcePathInfo?.configuredSourceReady) {
      statusClass = 'ready';
      text = 'Configured source is ready.';
    } else if (sourcePath && sameClientPath(sourcePath, detectedPath) && setupSourcePathInfo?.detectedSourceReady) {
      statusClass = 'detected';
      text = 'Detected Steam source is ready.';
    } else if (sourcePath) {
      statusClass = 'detected';
      text = 'This source path will be validated when setup runs.';
    } else if (effectivePath) {
      statusClass = 'detected';
      text = setupSourcePathInfo?.summary || 'Detected Beat Saber source.';
    }

    hint.textContent = text;
    hint.className = `setupPathHint ${statusClass}`;
  }

  if (setupButton) {
    const targetCount = configuredCount ?? getManagedInstanceInventoryCount(settings);
    const currentCreatedCount = createdCount ?? getCreatedInstanceCount(settings);
    const currentMissingCount = missingCount ?? Math.max(0, targetCount - currentCreatedCount);
    const needsProvision = currentMissingCount > 0;
    const needsSource = currentCreatedCount === 0;
    setupButton.hidden = !needsProvision;
    setupButton.disabled = Boolean(pendingSetupInstanceCreation || (needsSource && !sourcePath));
    setupButton.innerHTML = pendingSetupInstanceCreation
      ? '<span class="setupSpinner tiny" aria-hidden="true"></span>Running Setup'
      : escapeHtml(needsSource ? 'Run Setup' : `Create ${currentMissingCount} Missing Instance${currentMissingCount === 1 ? '' : 's'}`);
    setupButton.title = needsSource && !sourcePath
      ? 'Choose the Beat Saber source folder first.'
      : '';
  }
}

function renderSetupInstanceList(settings, configuredCount, createdCount, missingCount) {
  const rows = document.getElementById('setupInstanceRows');
  const summary = document.getElementById('setupInstanceSummary');
  const addButton = document.getElementById('setupWizardAddInstance');
  if (!rows) return;

  const instances = state?.instances || [];
  const enabledCount = instances
    .filter(instance => Number(instance.index) < configuredCount && getEffectiveSetupInstanceEnabled(instance))
    .length;

  if (summary) {
    summary.textContent = `${createdCount}/${configuredCount} created | ${Math.max(1, enabledCount)} enabled`;
  }

  rows.innerHTML = '';
  const creating = pendingSetupInstanceCreation;
  for (let index = 0; index < configuredCount; index++) {
    const instance = instances.find(item => Number(item.index) === index) || {
      index,
      name: formatInstanceName(index),
      enabled: true,
      launchDirectoryReady: false
    };
    const created = Boolean(instance.launchDirectoryReady);
    const isCreating = Boolean(
      creating &&
      !created &&
      Number(index) >= Number(creating.startIndex) &&
      Number(index) < Number(creating.targetCount));
    const storedEnabled = isInstanceEnabled(instance);
    const enabled = getEffectiveSetupInstanceEnabled(instance);
    const pendingEnabledChange = pendingSetupEnabledInstanceCount != null && enabled !== storedEnabled;
    const isLast = index === configuredCount - 1;
    const canRemove = configuredCount > minManagedInstanceCount && isLast;
    const busy = Boolean(
      state?.run?.isRunning ||
      state?.run?.cancellationRequested ||
      instance.gameProcessId ||
      instance.workerId ||
      isActiveStatus(instance.status) ||
      sameStatus(instance.status, 'Recording'));
    const statusLabel = isCreating ? 'Creating' : (created ? 'Created' : 'Missing');
    const enabledLabel = created
      ? (pendingEnabledChange
        ? (enabled ? 'Will enable' : 'Will disable')
        : (enabled ? 'Enabled' : 'Disabled'))
      : (isCreating ? 'Provisioning' : 'Not created');
    const locationLabel = created
      ? shortPath(instance.launchDirectory || settings.beatSaberInstancesRoot || '')
      : (isCreating
        ? 'Copying game files and repairing shared folders...'
        : (createdCount === 0 ? 'Create from the Beat Saber source folder.' : 'Create this managed copy from Instance 1.'));
    const row = document.createElement('div');
    row.className = `setupInstanceRow ${created ? '' : 'isMissing'} ${isCreating ? 'isCreating' : ''} ${enabled ? '' : 'isDisabled'}`;
    row.innerHTML = `
      <div class="setupInstanceIdentity">
        <span class="${isCreating ? 'setupSpinner' : `instanceDot ${created && enabled ? 'online' : 'idle'}`}" aria-hidden="true"></span>
        <div>
          <strong>${escapeHtml(formatInstanceDisplayName(instance, index))}</strong>
          <span>${escapeHtml(locationLabel)}</span>
        </div>
      </div>
      <div class="setupInstanceBadges">
        <span class="setupInstanceBadge ${isCreating ? 'isCreating' : (created ? 'isCreated' : 'isMissing')}">${escapeHtml(statusLabel)}</span>
        <span class="setupInstanceBadge ${enabled && created ? 'isEnabled' : ''}">${escapeHtml(enabledLabel)}</span>
      </div>
      <div class="setupInstanceActions">
        ${created ? `<button class="textButton" type="button" data-setup-instance-enabled="${index}" data-enabled="${enabled ? 'false' : 'true'}"${disabledAttr(isCreating || pendingSetupInstanceCreation || (enabled && enabledCount <= 1))}>${enabled ? 'Disable' : 'Enable'}</button>` : `<button class="textButton setupCreatingButton" type="button" data-setup-instance-create="${index}"${disabledAttr(isCreating || pendingSetupInstanceCreation)}>${isCreating ? '<span class="setupSpinner tiny" aria-hidden="true"></span>Creating' : 'Create'}</button>`}
        ${canRemove ? `<button class="textButton dangerText" type="button" data-setup-instance-remove="${index}"${disabledAttr(busy || pendingSetupInstanceCreation)}>Remove</button>` : ''}
      </div>
    `;
    rows.appendChild(row);
  }

  if (addButton) {
    const canAdd = configuredCount < maxManagedInstanceCount || missingCount > 0;
    addButton.hidden = !canAdd || createdCount === 0;
    addButton.disabled = Boolean(pendingSetupInstanceCreation);
    addButton.innerHTML = pendingSetupInstanceCreation
      ? '<span class="setupSpinner tiny" aria-hidden="true"></span>Creating Instance'
      : escapeHtml(missingCount > 0
        ? `Create ${missingCount} Missing Instance${missingCount === 1 ? '' : 's'}`
        : '+ Add Instance');
  }
}

function ensureGameSettingsPlacement() {
  const assistant = document.getElementById('setupAssistant');
  const advanced = document.getElementById('advancedSettingsDetails');
  const gameSettings = document.getElementById('gameSettings');
  if (!assistant || !advanced || !gameSettings) return;

  gameSettings.classList.add('setupGamePanel');
  gameSettings.classList.remove('span4');
  if (gameSettings.parentElement === assistant && advanced.nextElementSibling === gameSettings) return;

  assistant.insertBefore(gameSettings, advanced.nextElementSibling);
}

function renderFeedPresetList(settings, activeProfileId) {
  const list = document.getElementById('feedPresetList');
  if (!list) return;

  const selectedDisplay = getSelectedDisplay(settings);
  const supported = feedPresetDefinitions.filter(definition => feedPresetFitsDisplay(definition, selectedDisplay));
  const visibleDefinitions = supported.length
    ? feedPresetDefinitions.filter(definition => definition.tier === supported[supported.length - 1].tier)
    : feedPresetDefinitions;

  list.innerHTML = visibleDefinitions.map(definition => {
    const fits = feedPresetFitsDisplay(definition, selectedDisplay);
    const active = definition.profileId === activeProfileId;
    const status = fits ? 'Recommended' : formatPresetRequirement(definition);
    return `
      <button class="setupProfileButton feedPresetButton ${active ? 'active' : ''}" type="button" data-setup-profile="${escapeHtml(definition.profileId)}" ${fits ? '' : 'disabled'}>
        <strong>${escapeHtml(definition.title)}</strong>
        <span>${escapeHtml(definition.detail)}</span>
        <small>${escapeHtml(status)}</small>
      </button>
    `;
  }).join('');
}

function renderSetupMonitorGraphic(settings = {}) {
  const graphic = document.getElementById('setupMonitorGraphic');
  if (!graphic) return;

  const displays = getDetectedDisplays();
  const selectedIndex = Number(settings.monitorIndex ?? document.getElementById('monitorIndex')?.value ?? 1);
  if (!displays.length) {
    graphic.innerHTML = `
      <div class="setupMonitorGraphicEmpty">
        <strong>Display detection unavailable</strong>
        <span>Use the recording monitor selector above, then choose a feed preset.</span>
      </div>
    `;
    return;
  }

  const bounds = getDisplayBounds(displays);
  const cards = displays
    .map(display => renderSetupMonitorCard(display, selectedIndex, bounds))
    .join('');

  graphic.innerHTML = `
    <div class="setupMonitorStage">
      ${cards}
    </div>
  `;
}

function getDetectedDisplays() {
  return Array.isArray(displayInfo?.displays)
    ? displayInfo.displays
      .filter(display => Number.isFinite(Number(display.index)))
      .slice()
      .sort((first, second) => Number(first.index) - Number(second.index))
    : [];
}

function getDisplayBounds(displays) {
  const leftValues = displays.map(display => Number(display.left) || 0);
  const topValues = displays.map(display => Number(display.top) || 0);
  const rightValues = displays.map(display => (Number(display.left) || 0) + Math.max(1, Number(display.width) || 1));
  const bottomValues = displays.map(display => (Number(display.top) || 0) + Math.max(1, Number(display.height) || 1));
  const left = Math.min(...leftValues);
  const top = Math.min(...topValues);
  const right = Math.max(...rightValues);
  const bottom = Math.max(...bottomValues);
  return {
    left,
    top,
    width: Math.max(1, right - left),
    height: Math.max(1, bottom - top)
  };
}

function renderSetupMonitorCard(display, selectedIndex, bounds) {
  const index = Number(display.index);
  const active = Number.isFinite(index) && index === selectedIndex;
  const recommended = getRecommendedFeedPresetForDisplay(display);
  const left = (((Number(display.left) || 0) - bounds.left) / bounds.width) * 100;
  const top = (((Number(display.top) || 0) - bounds.top) / bounds.height) * 100;
  const width = (Math.max(1, Number(display.width) || 1) / bounds.width) * 100;
  const height = (Math.max(1, Number(display.height) || 1) / bounds.height) * 100;
  const monitorName = formatMonitorName(display);
  const deviceName = display?.friendlyName || buildDetectedMonitorLabel(display).replace(/^Monitor\s+\d+\s+-\s*/i, '');
  const resolution = display?.width && display?.height
    ? `${display.width} x ${display.height}`
    : 'Resolution unknown';
  const preset = recommended?.title || 'Choose feed preset';
  const primary = display?.isPrimary ? 'Primary display' : 'Detected display';
  const detail = deviceName
    ? `${deviceName} | ${resolution}`
    : resolution;
  const compact = width < 42 || height < 60;

  return `
    <button class="setupMonitorCard ${active ? 'active' : ''} ${compact ? 'compact' : ''}" type="button"
      data-setup-monitor-index="${escapeHtml(index)}"
      data-setup-monitor-profile="${escapeHtml(recommended?.profileId || '')}"
      aria-pressed="${active ? 'true' : 'false'}"
      style="--monitor-left:${left.toFixed(3)}%;--monitor-top:${top.toFixed(3)}%;--monitor-width:${width.toFixed(3)}%;--monitor-height:${height.toFixed(3)}%">
      <span class="setupMonitorCardChrome" aria-hidden="true"></span>
      <strong>${escapeHtml(monitorName)}</strong>
      <span>${escapeHtml(detail)}</span>
      <small>${escapeHtml(primary)} | ${escapeHtml(preset)}</small>
    </button>
  `;
}

function formatMonitorName(display) {
  const monitorNumber = Number(display?.monitorNumber);
  if (Number.isFinite(monitorNumber) && monitorNumber > 0) return `Monitor ${monitorNumber}`;

  const index = Number(display?.index);
  if (Number.isFinite(index)) return `Monitor ${index + 1}`;

  return 'Monitor';
}

function getRecommendedFeedPresetForDisplay(display) {
  const supported = feedPresetDefinitions.filter(definition => feedPresetFitsDisplay(definition, display));
  if (!supported.length) return null;

  const highestTier = supported[supported.length - 1].tier;
  const highestTierDefinitions = supported.filter(definition => definition.tier === highestTier);
  return highestTierDefinitions.find(definition => definition.profileId.startsWith('grid-'))
    || highestTierDefinitions[highestTierDefinitions.length - 1]
    || supported[supported.length - 1];
}

function applySetupMonitorPreset(monitorIndex, profileId = '') {
  const normalized = Number(monitorIndex);
  if (!Number.isFinite(normalized)) return;

  setValue('monitorIndex', normalized);
  const display = getDetectedDisplays().find(item => Number(item.index) === normalized);
  const profile = profileId || getRecommendedFeedPresetForDisplay(display)?.profileId || '';
  if (profile && setupProfiles[profile]) {
    applySetupProfile(profile);
  } else {
    markSettingsDirty();
    renderSetupAssistant();
  }

  showToast(`${formatMonitorSummary(normalized)} selected`);
}

function renderSelectedMonitorSummary(settings) {
  const summary = document.getElementById('selectedMonitorSummary');
  if (!summary) return;

  const selectedDisplay = getSelectedDisplay(settings);
  if (!selectedDisplay) {
    summary.textContent = displayInfo?.summary || 'Display detection unavailable; choose a feed manually in Advanced.';
    return;
  }

  const capability = classifyDisplayCapability(selectedDisplay);
  summary.textContent = `${buildDetectedMonitorLabel(selectedDisplay)} supports ${capability}.`;
}

function getSelectedDisplay(settings = {}) {
  const monitorIndex = Number(settings.monitorIndex);
  if (!Number.isFinite(monitorIndex)) return null;

  return (displayInfo?.displays || []).find(display => Number(display.index) === monitorIndex) || null;
}

function classifyDisplayCapability(display) {
  if (displaySupportsResolution(display, 5120, 2880)) return '1 x 5K or up to 4 x 1440p feeds';
  if (displaySupportsResolution(display, 3840, 2160)) return '1 x 4K or up to 4 x 1080p feeds';
  if (displaySupportsResolution(display, 2560, 1440)) return '1 x 1440p or up to 4 x 720p feeds';
  if (displaySupportsResolution(display, 1920, 1080)) return '1 x 1080p feed';
  if (displaySupportsResolution(display, 1280, 720)) return '1 x 720p feed';
  return 'custom capture sizing';
}

function feedPresetFitsDisplay(definition, display) {
  if (!display) return true;
  return displaySupportsResolution(display, definition.minWidth, definition.minHeight);
}

function displaySupportsResolution(display, minWidth, minHeight) {
  const width = Number(display?.width);
  const height = Number(display?.height);
  if (!Number.isFinite(width) || !Number.isFinite(height)) return false;
  return width >= minWidth && height >= minHeight;
}

function formatPresetRequirement(definition) {
  return `Needs ${definition.minWidth} x ${definition.minHeight} or larger`;
}

function setHidden(id, hidden) {
  const element = document.getElementById(id);
  if (!element) return;
  element.hidden = hidden;
}

function resolveSetupProfile(settings) {
  const launchPreset = resolveLaunchPreset(settings);
  if (launchPreset === '5k-monitor-2x2') return 'grid-5k';
  if (launchPreset === '4k-monitor-2x2') return 'grid-1080p';
  if (launchPreset === '720p-monitor-2x2') return 'grid-720p';
  if (launchPreset === '1440p-monitor-2x2') return 'grid-720p';
  if (launchPreset === 'single-5k') return 'single-5k';
  if (launchPreset === 'single-4k') return 'single-4k';
  if (launchPreset === 'single-720p') return 'single-720p';
  if (launchPreset === 'single-1440p') return 'single-1440p';
  if (launchPreset === 'single-1080p' || launchPreset === 'windowed-1080p') return 'single-1080p';
  return 'custom';
}

function getSetupProfileEnabledInstanceCount(id) {
  if (id === 'grid-5k' || id === 'grid-1080p' || id === 'grid-720p' || id === 'quad-4k' || id === 'quad-1440p' || id === 'quad-5k') {
    return maxManagedInstanceCount;
  }

  if (id === 'single-720p' || id === 'single-1080p' || id === 'single-1440p' || id === 'single-4k' || id === 'single-5k') {
    return minManagedInstanceCount;
  }

  return null;
}

function getEffectiveSetupInstanceEnabled(instance) {
  if (pendingSetupEnabledInstanceCount == null) return isInstanceEnabled(instance);
  return Number(instance.index) < pendingSetupEnabledInstanceCount;
}

function formatSetupProfile(id) {
  if (id === 'grid-5k' || id === 'quad-5k') return '4x 1440p streams (5k monitor)';
  if (id === 'grid-1080p' || id === 'quad-4k') return '4x 1080p streams (4k monitor)';
  if (id === 'grid-720p' || id === 'quad-1440p') return '4x 720p streams (1440p monitor)';
  if (id === 'single-720p') return '1x 720p stream (720p monitor)';
  if (id === 'single-4k') return '1x 4K stream (4k monitor)';
  if (id === 'single-1440p') return '1x 1440p stream (1440p monitor)';
  if (id === 'single-5k') return '1x 5K stream (5k monitor)';
  if (id === 'single-1080p') return '1x 1080p stream (4k monitor)';
  return 'Custom';
}

function buildSetupWizardChecklist(settings) {
  const instances = state.instances || [];
  const configuredCount = clampManagedInstanceCount(settings.instanceCount || instances.length || 1);
  const createdCount = getCreatedInstanceCount(settings);
  const runtimeCount = createdCount > 0 ? createdCount : configuredCount;
  const activeWorkers = instances.filter(instance => instance.index < configuredCount && instance.workerId && (createdCount === 0 || instance.launchDirectoryReady)).length;
  const processCount = instances.filter(instance => instance.index < configuredCount && instance.gameProcessId && (createdCount === 0 || instance.launchDirectoryReady)).length;
  const failedInstance = instances.find(instance => instance.gameLaunchError || instance.audioRoutingError);
  const provision = state.instanceProvision || {};
  const baseline = state.instanceBaseline || {};
  const audioMode = settings.audioMode || 'ProcessLoopback';
  const root = settings.beatSaberInstancesRoot || 'No instance root';
  const instancePlanStatus = createdCount >= configuredCount ? 'Ready' : (createdCount > 0 ? 'Missing' : 'Check');
  const provisionStatus = createdCount >= configuredCount && sameStatus(provision.status, 'Ready') ? 'Ready' : (createdCount > 0 ? 'Missing' : 'Check');
  const workerStatus = activeWorkers >= runtimeCount ? 'Ready' : 'Waiting';
  const launchStatus = failedInstance
    ? 'Failed'
    : (processCount >= runtimeCount ? 'Ready' : 'Waiting');
  const audioStatus = sameStatus(audioMode, 'ProcessLoopback')
    ? (processCount >= runtimeCount ? 'Ready' : 'Waiting')
    : (settings.requireAudioForRun === false ? 'Off' : 'Check');
  const baselineStatus = baseline.status || 'Unchecked';

  return [
    {
      label: 'Instance plan',
      status: instancePlanStatus,
      detail: `${createdCount}/${configuredCount} created under ${shortPath(root)}`
    },
    {
      label: 'Managed copy',
      status: provisionStatus,
      detail: provision.summary || 'Managed instances have not been created.'
    },
    {
      label: 'Workers',
      status: workerStatus,
      detail: `${activeWorkers}/${runtimeCount} online`
    },
    {
      label: 'Launch',
      status: launchStatus,
      detail: failedInstance
        ? `${failedInstance.name || 'Instance'}: ${failedInstance.gameLaunchError || failedInstance.audioRoutingError}`
        : `${processCount}/${runtimeCount} Beat Saber process ids known`
    },
    {
      label: 'Audio',
      status: audioStatus,
      detail: sameStatus(audioMode, 'ProcessLoopback')
        ? 'ProcessLoopback per game process'
        : 'Audio disabled'
    },
    {
      label: 'Baseline',
      status: baselineStatus,
      detail: baseline.summary || 'Baseline has not been checked.'
    }
  ];
}

function buildCurrentSettingsPreview() {
  if (!state) return {};

  try {
    return { ...state.settings, ...buildSettingsRequest() };
  } catch {
    return state.settings || {};
  }
}

function shortPath(value) {
  const text = String(value ?? '');
  if (!text) return '';
  const parts = text.split(/[\\/]/).filter(Boolean);
  if (parts.length <= 2) return text;
  return `${parts[parts.length - 2]}\\${parts[parts.length - 1]}`;
}

function showSetupAssistant() {
  writeSetupAssistantHidden(false);
  activateView('settings');
  renderSetupAssistant();
}

function updateAdvancedSettingsToggle() {
  const panel = document.getElementById('advancedSettingsDetails');
  const button = document.getElementById('setupWizardAdvanced');
  if (!panel || !button) return;

  const isOpen = !panel.hidden;
  button.textContent = isOpen ? 'Hide Advanced' : 'Advanced Settings';
  button.setAttribute('aria-expanded', String(isOpen));
}

function setAdvancedSettingsOpen(open, options = {}) {
  const panel = document.getElementById('advancedSettingsDetails');
  if (!panel) return;

  panel.hidden = !open;
  updateAdvancedSettingsToggle();
  if (open && options.scroll !== false) {
    panel.scrollIntoView({ block: 'start', behavior: 'smooth' });
  }
}

function toggleAdvancedSettings() {
  const panel = document.getElementById('advancedSettingsDetails');
  if (!panel) return;

  setAdvancedSettingsOpen(panel.hidden);
}

function showAdvancedSettings() {
  setAdvancedSettingsOpen(true);
}

function revealSettingsTarget(targetId) {
  const panel = document.getElementById('advancedSettingsDetails');
  const target = document.getElementById(targetId);
  if (!panel || !target) return;

  if (panel === target || panel.contains(target)) {
    setAdvancedSettingsOpen(true, { scroll: false });
  }
}

async function persistSettings() {
  const enabledInstanceCount = pendingSetupEnabledInstanceCount;
  const request = buildSettingsRequest();
  const restartRecommendation = getSettingsRestartRecommendation(state?.settings, request);
  await postJson('/api/settings', request);
  if (enabledInstanceCount != null) {
    await applySetupEnabledInstanceCount(enabledInstanceCount);
    pendingSetupEnabledInstanceCount = null;
    render();
  }
  settingsDirty = false;
  updateSettingsDirtyBadge();
  return await promptForSettingsRestart(restartRecommendation);
}

function getSettingsRestartRecommendation(previous, next) {
  const restartLabels = [];
  const previousSettings = previous || {};
  const nextSettings = next || {};

  for (const [fieldName, label] of Object.entries(restartRecommendedSettings)) {
    if (normalizeSettingsRestartValue(previousSettings[fieldName]) !==
        normalizeSettingsRestartValue(nextSettings[fieldName])) {
      addUniqueRestartLabel(restartLabels, label);
    }
  }

  for (const fieldName of getGamePresentationRestartFields(
    previousSettings.gamePresentation,
    nextSettings.gamePresentation)) {
    addUniqueRestartLabel(restartLabels, formatGamePresentationFieldName(fieldName));
  }

  if (restartLabels.length === 0) {
    return null;
  }

  return {
    restartLabels
  };
}

function addUniqueRestartLabel(labels, label) {
  if (!label || labels.includes(label)) return;
  labels.push(label);
}

function getGamePresentationRestartFields(previous, next) {
  const restartFields = [];
  const previousSettings = previous || {};
  const nextSettings = next || {};
  const fieldNames = new Set([
    ...Object.keys(gamePresentationDefaults),
    ...Object.keys(previousSettings),
    ...Object.keys(nextSettings),
    ...liveSyncedGamePresentationFields,
    ...restartRecommendedGamePresentationFields
  ]);

  for (const fieldName of fieldNames) {
    if (!restartRecommendedGamePresentationFields.has(fieldName)) continue;

    if (normalizeGamePresentationValue(previousSettings, fieldName) !==
        normalizeGamePresentationValue(nextSettings, fieldName)) {
      restartFields.push(fieldName);
    }
  }

  return restartFields;
}

function normalizeGamePresentationValue(settings, fieldName) {
  const value = Object.prototype.hasOwnProperty.call(settings, fieldName)
    ? settings[fieldName]
    : gamePresentationDefaults[fieldName];
  return normalizeSettingsRestartValue(value);
}

function normalizeSettingsRestartValue(value) {
  if (typeof value === 'number') {
    return Number.isFinite(value) ? String(Math.round(value * 100000) / 100000) : '';
  }

  if (typeof value === 'string') {
    return value.trim();
  }

  return JSON.stringify(value ?? null);
}

async function promptForSettingsRestart(recommendation) {
  if (!recommendation) return false;

  const activeInstances = (state?.instances || [])
    .filter(instance => instance?.gameProcessId || instance?.workerId);
  if (activeInstances.length === 0) return false;

  const fieldList = recommendation.restartLabels
    .slice(0, 4)
    .join(', ');
  const suffix = recommendation.restartLabels.length > 4 ? ', and more' : '';
  const changedText = fieldList
    ? `Saved settings changed ${fieldList}${suffix}.`
    : 'Saved settings changed values that active Beat Saber workers may not fully reload.';

  if (state?.run?.isRunning || state?.run?.cancellationRequested) {
    window.alert(`${changedText} Restart the managed Beat Saber workers after this run so the changes take effect.`);
    showToast('Settings saved; restart workers after this run');
    return true;
  }

  const message = `${changedText} Restart managed Beat Saber workers now so the changes take effect?`;

  if (!window.confirm(message)) {
    showToast('Settings saved; restart workers when convenient');
    return true;
  }

  for (const instance of activeInstances) {
    await postJson(`/api/instances/${Number(instance.index)}/quit`);
  }

  await postJson('/api/instances/launch');
  showToast('Settings saved; workers restarted');
  return true;
}

function formatGamePresentationFieldName(fieldName) {
  const labels = {
    noTextsAndHuds: 'HUD visibility',
    advancedHud: 'advanced HUD',
    reduceDebris: 'debris',
    noFailEffects: 'fail effects',
    saberTrailIntensity: 'saber trails',
    leftSaberColor: 'left saber color',
    rightSaberColor: 'right saber color',
    lightColorA: 'light color A',
    lightColorB: 'light color B',
    boostLightColorA: 'boost light color A',
    boostLightColorB: 'boost light color B',
    wallColor: 'wall color',
    noteJumpDurationType: 'note jump mode',
    noteJumpFixedDuration: 'note jump duration',
    noteJumpStartBeatOffset: 'note jump offset',
    applyJdFixerSettings: 'JDFixer',
    jdFixerMode: 'JDFixer mode',
    jdFixerJumpDistance: 'JDFixer jump distance',
    jdFixerReactionTime: 'JDFixer reaction time',
    hideNoteSpawnEffect: 'note spawn effect',
    adaptiveSfx: 'adaptive SFX',
    arcsHapticFeedback: 'arc haptics',
    arcVisibility: 'arc visibility',
    environmentEffectsFilterDefaultPreset: 'default environment effects',
    environmentEffectsFilterExpertPlusPreset: 'Expert+ environment effects',
    headsetHapticIntensity: 'headset haptics'
  };

  return labels[fieldName] || fieldName;
}

async function applySetupEnabledInstanceCount(enabledInstanceCount) {
  const targetEnabledCount = clampManagedInstanceCount(enabledInstanceCount);
  const configuredCount = getManagedInstanceInventoryCount();
  const instances = state?.instances || [];
  for (let index = 0; index < configuredCount; index++) {
    if (index >= targetEnabledCount) continue;
    const instance = instances.find(item => Number(item.index) === index);
    if (instance && !isInstanceEnabled(instance)) {
      await postJson(`/api/instances/${index}/enabled`, { enabled: true });
    }
  }

  for (let index = configuredCount - 1; index >= 0; index--) {
    if (index < targetEnabledCount) continue;
    const instance = (state?.instances || []).find(item => Number(item.index) === index);
    if (instance && isInstanceEnabled(instance)) {
      await postJson(`/api/instances/${index}/enabled`, { enabled: false });
    }
  }
}

async function runSetupWizardBaselineCheck() {
  await persistSettings();
  await postJson('/api/instances/baseline/check');
  showToast(`Baseline ${state.instanceBaseline?.status || 'checked'}`);
}

async function runSetupWizardProvision() {
  const settings = buildCurrentSettingsPreview();
  const configuredCount = getManagedInstanceInventoryCount(settings);
  const createdCount = getCreatedInstanceCount(settings);
  const missingCount = Math.max(0, configuredCount - createdCount);
  if (missingCount <= 0) {
    showToast('Managed instances are already created');
    return;
  }

  const sourcePath = getSetupSourcePathValue(settings);
  const createMissingOnly = createdCount > 0;
  if (!createMissingOnly && !sourcePath) {
    throw new Error('Choose the Beat Saber source folder first.');
  }

  pendingSetupInstanceCreation = {
    startIndex: createMissingOnly ? createdCount : 0,
    targetCount: configuredCount
  };
  render();
  try {
    await persistSettings();
    await postJson('/api/instances/provision', {
      sourceBeatSaberPath: sourcePath,
      instanceCount: configuredCount,
      createMissingOnly,
      overwriteExisting: false,
      copyExistingSongs: false
    });
    await loadSetupSourcePath().catch(() => {});
    showToast(state.instanceProvision?.summary || 'Managed instances created');
  } finally {
    pendingSetupInstanceCreation = null;
    render();
  }
}

async function runSetupWizardAddInstance() {
  const settings = buildCurrentSettingsPreview();
  const configuredCount = clampManagedInstanceCount(settings.instanceCount);
  const createdCount = getCreatedInstanceCount(settings);
  const missingCount = Math.max(0, configuredCount - createdCount);
  const nextCount = missingCount > 0 ? configuredCount : configuredCount + 1;
  if (nextCount > maxManagedInstanceCount) {
    throw new Error(`Managed instances are limited to ${maxManagedInstanceCount}.`);
  }

  pendingSetupInstanceCreation = {
    startIndex: missingCount > 0 ? createdCount : nextCount - 1,
    targetCount: nextCount
  };
  setValue('instanceCount', nextCount);
  render();
  try {
    await persistSettings();
    await postJson('/api/instances/provision', {
      instanceCount: nextCount,
      createMissingOnly: true,
      overwriteExisting: false,
      copyExistingSongs: false
    });
    showToast(state.instanceProvision?.summary || 'Managed instance created');
  } finally {
    pendingSetupInstanceCreation = null;
    render();
  }
}

async function setSetupInstanceEnabled(index, enabled) {
  pendingSetupEnabledInstanceCount = null;
  const instance = (state.instances || []).find(item => Number(item.index) === Number(index));
  await postJson(`/api/instances/${Number(index)}/enabled`, { enabled });
  showToast(`${formatInstanceDisplayName(instance, index)} ${enabled ? 'enabled' : 'disabled'}`);
}

async function removeSetupInstance(index) {
  const instance = (state.instances || []).find(item => Number(item.index) === Number(index));
  const name = formatInstanceDisplayName(instance, index);
  if (!window.confirm(`Remove ${name} from the managed recorder workspace? This deletes only that managed copy, not your real Beat Saber install.`)) {
    return;
  }

  await persistSettings();
  await postJson(`/api/instances/${Number(index)}/remove`);
  showToast(`${name} removed`);
}

async function runSetupWizardLaunchOnly() {
  await persistSettings();
  await postJson('/api/instances/launch');
  showToast('Game launch requested');
}

async function runSetupWizardVerify() {
  await persistSettings();
  await postJson('/api/instances/baseline/check');
  await postJson('/api/instances/launch');
  showToast('Launch requested; verification is updating');
}

async function postJson(url, body = {}) {
  const response = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  });
  if (!response.ok) throw new Error(await response.text());
  state = await response.json();
  render();
  return state;
}

function buildSettingsRequest() {
  const instanceCount = getManagedInstanceInventoryCount();

  return {
    recordingOutputDirectory: getTextOrSetting('recordingOutputDirectory', 'recordingOutputDirectory'),
    instanceCount,
    maxConcurrentRecordings: instanceCount,
    requireAllWorkersReady: document.getElementById('requireAllWorkersReady').checked,
    requireMatchingInstanceBaseline: document.getElementById('requireMatchingInstanceBaseline').checked,
    sharedCustomLevelsDirectory: getTextOrSetting('sharedCustomLevelsDirectory', 'sharedCustomLevelsDirectory'),
    sharedCustomWipLevelsDirectory: getTextOrSetting('sharedCustomWipLevelsDirectory', 'sharedCustomWipLevelsDirectory'),
    shareCustomSabers: getCheckedOrSetting('shareCustomSabers', 'shareCustomSabers'),
    sharedCustomSabersDirectory: getTextOrSetting('sharedCustomSabersDirectory', 'sharedCustomSabersDirectory'),
    shareCustomNotes: getCheckedOrSetting('shareCustomNotes', 'shareCustomNotes'),
    sharedCustomNotesDirectory: getTextOrSetting('sharedCustomNotesDirectory', 'sharedCustomNotesDirectory'),
    shareCustomPlatforms: getCheckedOrSetting('shareCustomPlatforms', 'shareCustomPlatforms'),
    sharedCustomPlatformsDirectory: getTextOrSetting('sharedCustomPlatformsDirectory', 'sharedCustomPlatformsDirectory'),
    shareCustomAvatars: getCheckedOrSetting('shareCustomAvatars', 'shareCustomAvatars'),
    sharedCustomAvatarsDirectory: getTextOrSetting('sharedCustomAvatarsDirectory', 'sharedCustomAvatarsDirectory'),
    shareCustomWalls: getCheckedOrSetting('shareCustomWalls', 'shareCustomWalls'),
    sharedCustomWallsDirectory: getTextOrSetting('sharedCustomWallsDirectory', 'sharedCustomWallsDirectory'),
    shareCustomBombs: getCheckedOrSetting('shareCustomBombs', 'shareCustomBombs'),
    sharedCustomBombsDirectory: getTextOrSetting('sharedCustomBombsDirectory', 'sharedCustomBombsDirectory'),
    targetFps: getNumber('targetFps'),
    captureWidth: getNumber('captureWidth'),
    captureHeight: getNumber('captureHeight'),
    videoBitrateKbps: getNumber('videoBitrateKbps'),
    outputFormat: getText('outputFormat'),
    monitorIndex: getNumber('monitorIndex'),
    encoder: getText('encoder'),
    qualityMode: getText('qualityMode'),
    captureEngine: getText('captureEngine') || defaultCaptureEngine,
    audioMode: getText('audioMode'),
    requireAudioForRun: document.getElementById('requireAudioForRun').checked,
    audioBitrateKbps: getNumber('audioBitrateKbps'),
    audioSampleRate: getNumber('audioSampleRate'),
    audioChannels: 2,
    audioLevelMode: getText('audioLevelMode'),
    audioTargetLevelDb: getNumber('audioTargetLevelDb'),
    beatSaberInstancesRoot: getTextOrSetting('beatSaberInstancesRoot', 'beatSaberInstancesRoot'),
    sourceBeatSaberPath: getSetupSourcePathValue(state.settings),
    beatSaberInstanceNamePrefix: managedInstanceNamePrefix,
    beatSaberLaunchPreset: getText('beatSaberLaunchPreset'),
    beatSaberLaunchArguments: getText('beatSaberLaunchArguments'),
    manageDisplayScale: document.getElementById('manageDisplayScale').checked,
    recordingDisplayScalePercent: getNumber('recordingDisplayScalePercent'),
    restoreDisplayScalePercent: getNumberOrSetting(
      'restoreDisplayScalePercent',
      'restoreDisplayScalePercent',
      150),
    hideTaskbarDuringRun: document.getElementById('hideTaskbarDuringRun').checked,
    disableScoreSubmissions: document.getElementById('disableScoreSubmissions').checked,
    suppressScoreSaberReplayUi: document.getElementById('suppressScoreSaberReplayUi').checked,
    delayBetweenRecordingsSeconds: getConfiguredInterReplayGapSeconds(),
    gamePresentation: {
      noHud: document.getElementById('noHud').checked,
      loadPlayerEnvironment: document.getElementById('loadPlayerEnvironment').checked,
      loadPlayerJumpDistance: document.getElementById('loadPlayerJumpDistance').checked,
      overrideReplayPlayerSettings: document.getElementById('overrideReplayPlayerSettings').checked,
      ignoreModifiers: document.getElementById('ignoreModifiers').checked,
      showHead: document.getElementById('showHead').checked,
      showLeftSaber: document.getElementById('showLeftSaber').checked,
      showRightSaber: document.getElementById('showRightSaber').checked,
      showWatermark: document.getElementById('showWatermark').checked,
      showTimelineMisses: getGameCheckedOrSetting('showTimelineMisses', 'showTimelineMisses', true),
      showTimelineBombs: getGameCheckedOrSetting('showTimelineBombs', 'showTimelineBombs', true),
      showTimelinePauses: getGameCheckedOrSetting('showTimelinePauses', 'showTimelinePauses', true),
      sfxVolume: getNumber('sfxVolume'),
      noTextsAndHuds: document.getElementById('noTextsAndHuds').checked,
      advancedHud: document.getElementById('noTextsAndHuds').checked ? false : document.getElementById('advancedHud').checked,
      reduceDebris: document.getElementById('reduceDebris').checked,
      noFailEffects: getGameCheckedOrSetting('noFailEffects', 'noFailEffects', false),
      saberTrailIntensity: getNumber('saberTrailIntensity'),
      leftSaberColor: getText('leftSaberColor'),
      rightSaberColor: getText('rightSaberColor'),
      lightColorA: getText('lightColorA'),
      lightColorB: getText('lightColorB'),
      boostLightColorA: getText('boostLightColorA'),
      boostLightColorB: getText('boostLightColorB'),
      wallColor: getText('wallColor'),
      noteJumpDurationType: getText('noteJumpDurationType'),
      noteJumpFixedDuration: getNumber('noteJumpFixedDuration'),
      noteJumpStartBeatOffset: getNumber('noteJumpStartBeatOffset'),
      applyJdFixerSettings: document.getElementById('applyJdFixerSettings').checked,
      jdFixerMode: getText('jdFixerMode'),
      jdFixerJumpDistance: getNumber('jdFixerJumpDistance'),
      jdFixerReactionTime: getNumber('jdFixerReactionTime'),
      hideNoteSpawnEffect: document.getElementById('hideNoteSpawnEffect').checked,
      adaptiveSfx: document.getElementById('adaptiveSfx').checked,
      arcsHapticFeedback: getGameCheckedOrSetting('arcsHapticFeedback', 'arcsHapticFeedback', true),
      arcVisibility: getText('arcVisibility'),
      environmentEffectsFilterDefaultPreset: getText('environmentEffectsFilterDefaultPreset'),
      environmentEffectsFilterExpertPlusPreset: getText('environmentEffectsFilterExpertPlusPreset'),
      headsetHapticIntensity: getNumberOrSetting('headsetHapticIntensity', 'headsetHapticIntensity', 0.7)
    }
  };
}

async function saveSettings() {
  const handledRestartPrompt = await persistSettings();
  if (!handledRestartPrompt) {
    showToast('Settings saved');
  }
}

ensureGameSettingsPlacement();

document.getElementById('saveSettings').addEventListener('click', () => runAction(saveSettings));
document.getElementById('setupWizardAdvanced')?.addEventListener('click', toggleAdvancedSettings);
document.getElementById('unsavedSettingsPrompt')?.addEventListener('click', () => runAction(saveSettings));
document.getElementById('setupWizardRunSetup')?.addEventListener('click', () => runAction(runSetupWizardProvision));
document.getElementById('setupWizardAddInstance')?.addEventListener('click', () => runAction(runSetupWizardAddInstance));
document.getElementById('setupWizardVerify')?.addEventListener('click', () => runAction(runSetupWizardVerify));
document.getElementById('setupWizardCheckBaseline')?.addEventListener('click', () => runAction(runSetupWizardBaselineCheck));
document.getElementById('setupWizardLaunchOnly')?.addEventListener('click', () => runAction(runSetupWizardLaunchOnly));
document.getElementById('showSetupAssistant')?.addEventListener('click', showSetupAssistant);
document.getElementById('setupUseDetectedSource')?.addEventListener('click', () => {
  const detectedPath = setupSourcePathInfo?.detectedSourceBeatSaberPath || setupSourcePathInfo?.effectiveSourceBeatSaberPath || '';
  if (!detectedPath) {
    showToast('No Steam Beat Saber source detected');
    return;
  }

  setValue('setupSourceBeatSaberPath', detectedPath);
  markSettingsDirty();
  renderSetupAssistant();
});

document.addEventListener('click', event => {
  const profileButton = event.target.closest('[data-setup-profile]');
  if (profileButton && !profileButton.disabled) {
    applySetupProfile(profileButton.dataset.setupProfile || '');
    return;
  }

  const monitorButton = event.target.closest('[data-setup-monitor-index]');
  if (monitorButton && !monitorButton.disabled) {
    applySetupMonitorPreset(
      monitorButton.dataset.setupMonitorIndex,
      monitorButton.dataset.setupMonitorProfile || '');
    return;
  }

  const createButton = event.target.closest('[data-setup-instance-create]');
  if (createButton && !createButton.disabled) {
    runAction(runSetupWizardAddInstance);
    return;
  }

  const enabledButton = event.target.closest('[data-setup-instance-enabled]');
  if (enabledButton && !enabledButton.disabled) {
    runAction(() => setSetupInstanceEnabled(
      enabledButton.dataset.setupInstanceEnabled,
      enabledButton.dataset.enabled === 'true'));
    return;
  }

  const removeButton = event.target.closest('[data-setup-instance-remove]');
  if (removeButton && !removeButton.disabled) {
    runAction(() => removeSetupInstance(removeButton.dataset.setupInstanceRemove));
  }
});

document.querySelectorAll('.setupWizardGrid input').forEach(element => {
  if (element.type === 'hidden') return;

  element.addEventListener('input', () => {
    markSettingsDirty();
    renderSetupAssistant();
  });
  element.addEventListener('change', () => {
    markSettingsDirty();
    renderSetupAssistant();
  });
});

document.querySelector('[data-settings-jump]')?.addEventListener('click', () => {
  activateView('settings');
});

document.querySelectorAll('[data-view-target]').forEach(button => {
  button.addEventListener('click', () => activateView(button.dataset.viewTarget || 'run'));
});

document.querySelectorAll('[data-settings-target]').forEach(button => {
  button.addEventListener('click', () => {
    const targetId = button.dataset.settingsTarget || 'setupAssistant';
    activateView('settings');
    requestAnimationFrame(() => {
      revealSettingsTarget(targetId);
      requestAnimationFrame(() => {
        document.getElementById(targetId)?.scrollIntoView({ block: 'start', behavior: 'smooth' });
      });
    });
  });
});

function activateView(view, updateHash = true) {
  const requestedView = view === 'setup' ? 'settings' : view;
  const nextView = ['run', 'collections', 'settings', 'diagnostics'].includes(requestedView) ? requestedView : 'run';
  activeView = nextView;
  updateActiveView();
  if (updateHash) {
    history.replaceState(null, '', nextView === 'run' ? location.pathname : `#${nextView}`);
  }
}

function updateActiveView() {
  document.body.dataset.activeView = activeView;
  document.body.classList.toggle('settingsActive', activeView === 'settings');
  document.querySelectorAll('[data-view-panel]').forEach(panel => {
    panel.classList.toggle('active', panel.dataset.viewPanel === activeView);
  });
  document.querySelectorAll('[data-view-target]').forEach(button => {
    button.classList.toggle('active', button.dataset.viewTarget === activeView);
  });
}

function markSettingsDirty() {
  if (isRendering) return;
  settingsDirty = true;
  updateSettingsDirtyBadge();
}

function updateSettingsDirtyBadge() {
  const badge = document.getElementById('settingsDirtyBadge');
  const prompt = document.getElementById('unsavedSettingsPrompt');

  if (badge) {
    badge.textContent = settingsDirty ? 'Unsaved' : 'Saved';
    badge.classList.toggle('dirty', settingsDirty);
  }

  if (prompt) {
    prompt.hidden = !settingsDirty;
  }
}

document.getElementById('beatSaberLaunchPreset').addEventListener('change', event => {
  const id = event.target.value;
  if (id !== 'custom') applyLaunchPreset(id);
});

for (const fieldId of launchPresetFieldIds) {
  const element = document.getElementById(fieldId);
  if (!element) continue;

  element.addEventListener('input', markLaunchPresetCustom);
  element.addEventListener('change', markLaunchPresetCustom);
}

document.querySelectorAll('.settingsPanel input, .settingsPanel select, .settingsPanel textarea, .setupAdvancedPanel input, .setupAdvancedPanel select, .setupAdvancedPanel textarea, .setupGamePanel input, .setupGamePanel select, .setupGamePanel textarea').forEach(element => {
  element.addEventListener('input', markSettingsDirty);
  element.addEventListener('change', markSettingsDirty);
});
document.querySelectorAll('[data-game-value]').forEach(element => {
  element.addEventListener('input', () => updateGameValueLabel(element.id));
  element.addEventListener('change', () => updateGameValueLabel(element.id));
});
document.getElementById('noTextsAndHuds')?.addEventListener('change', updateAdvancedHudAvailability);
document.getElementById('manageDisplayScale')?.addEventListener('change', updateDisplayScaleAvailability);
document.getElementById('noteJumpDurationType')?.addEventListener('change', updateNoteJumpDurationAvailability);
document.getElementById('applyJdFixerSettings')?.addEventListener('change', updateJdFixerAvailability);
document.getElementById('jdFixerMode')?.addEventListener('change', updateJdFixerAvailability);
document.getElementById('audioLevelMode').addEventListener('change', updateAudioLevelTargetConstraints);
document.getElementById('captureEngine')?.addEventListener('change', updateCaptureEngineWarning);
document.getElementById('colorPresetSelect')?.addEventListener('change', updateColorPresetActions);
document.getElementById('applyColorPreset')?.addEventListener('click', () => runAction(async () => applyColorPreset()));
document.getElementById('saveColorPreset')?.addEventListener('click', () => runAction(saveCurrentColorPreset));
document.getElementById('deleteColorPreset')?.addEventListener('click', () => runAction(deleteSelectedColorPreset));
document.getElementById('monitorIndex')?.addEventListener('change', () => {
  markSettingsDirty();
  renderSetupAssistant();
});

document.getElementById('resolutionPreset').addEventListener('change', event => {
  const value = event.target.value;
  if (value === 'custom') return;
  const [width, height] = value.split('x').map(Number);
  setValue('captureWidth', width);
  setValue('captureHeight', height);
  markLaunchPresetCustom();
});

document.getElementById('queueSearchInput')?.addEventListener('input', event => {
  queueSearchText = event.target.value;
  editingQueueId = null;
  renderQueue();
});

const replayFileInput = document.getElementById('replayFiles');
const queueReplayFileInput = document.getElementById('queueReplayFiles');
const collectionReplayFileInput = document.getElementById('collectionReplayFiles');
const replayDrop = document.querySelector('.fileDrop');
const queueDrop = document.querySelector('.queuePanel');
const collectionDropTarget = document.getElementById('collectionDropTarget');
let replayDragDepth = 0;
let queueDragDepth = 0;
let collectionDragDepth = 0;

replayFileInput.addEventListener('change', event => {
  selectReplayFiles(event.target.files);
});

queueReplayFileInput.addEventListener('change', event => {
  if (!selectReplayFiles(event.target.files)) {
    showToast('Choose replay files');
    return;
  }

  runAction(importSelectedReplays);
});

collectionReplayFileInput?.addEventListener('change', event => {
  if (!selectCollectionReplayFiles(event.target.files)) {
    showToast('Choose replay files');
    return;
  }

  runAction(importSelectedReplaysToCollection);
});

replayDrop.addEventListener('dragenter', event => {
  event.preventDefault();
  event.stopPropagation();
  replayDragDepth++;
  replayDrop.classList.add('dragOver');
  if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
});

replayDrop.addEventListener('dragover', event => {
  event.preventDefault();
  event.stopPropagation();
  replayDrop.classList.add('dragOver');
  if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
});

replayDrop.addEventListener('dragleave', event => {
  event.preventDefault();
  event.stopPropagation();
  replayDragDepth = Math.max(0, replayDragDepth - 1);
  if (replayDragDepth === 0) replayDrop.classList.remove('dragOver');
});

replayDrop.addEventListener('drop', event => {
  event.preventDefault();
  event.stopPropagation();
  replayDragDepth = 0;
  replayDrop.classList.remove('dragOver');
  if (!selectReplayFiles(event.dataTransfer?.files)) {
    showToast('Drop replay files');
    return;
  }

  runAction(importSelectedReplays);
});

function openQueueReplayPicker() {
  queueReplayFileInput.value = '';
  queueReplayFileInput.click();
}

function openCollectionReplayPicker() {
  if (!getSelectedCollectionId()) {
    showToast('Create or select a collection first');
    return;
  }

  if (collectionReplayFileInput) {
    collectionReplayFileInput.value = '';
    collectionReplayFileInput.click();
  }
}

function bindQueueImportDropTarget(target) {
  let dragDepth = 0;

  target.addEventListener('dragenter', event => {
    event.preventDefault();
    event.stopPropagation();
    dragDepth++;
    target.classList.add('dragOver');
    if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
  });

  target.addEventListener('dragover', event => {
    event.preventDefault();
    event.stopPropagation();
    target.classList.add('dragOver');
    if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
  });

  target.addEventListener('dragleave', event => {
    event.preventDefault();
    event.stopPropagation();
    dragDepth = Math.max(0, dragDepth - 1);
    if (dragDepth === 0) target.classList.remove('dragOver');
  });

  target.addEventListener('drop', event => {
    event.preventDefault();
    event.stopPropagation();
    dragDepth = 0;
    target.classList.remove('dragOver');
    if (!selectReplayFiles(event.dataTransfer?.files)) {
      showToast('Drop replay files');
      return;
    }

    runAction(importSelectedReplays);
  });
}

function bindCollectionImportDropTarget(target) {
  if (!target) return;

  target.addEventListener('dragenter', event => {
    if (!isFileTransfer(event.dataTransfer)) return;
    event.preventDefault();
    event.stopPropagation();
    collectionDragDepth++;
    target.classList.add('dragOver');
    if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
  });

  target.addEventListener('dragover', event => {
    if (!isFileTransfer(event.dataTransfer)) return;
    event.preventDefault();
    event.stopPropagation();
    target.classList.add('dragOver');
    if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
  });

  target.addEventListener('dragleave', event => {
    if (!isFileTransfer(event.dataTransfer)) return;
    event.preventDefault();
    event.stopPropagation();
    collectionDragDepth = Math.max(0, collectionDragDepth - 1);
    if (collectionDragDepth === 0) target.classList.remove('dragOver');
  });

  target.addEventListener('drop', event => {
    if (!isFileTransfer(event.dataTransfer)) return;
    event.preventDefault();
    event.stopPropagation();
    collectionDragDepth = 0;
    target.classList.remove('dragOver');
    if (!getSelectedCollectionId()) {
      showToast('Create or select a collection first');
      return;
    }

    if (!selectCollectionReplayFiles(event.dataTransfer?.files)) {
      showToast('Drop replay files');
      return;
    }

    runAction(importSelectedReplaysToCollection);
  });
}

function isFileTransfer(dataTransfer) {
  if (!dataTransfer) return false;
  const types = Array.from(dataTransfer.types || []);
  if (types.includes('Files')) return true;
  return Array.from(dataTransfer.items || []).some(item => item.kind === 'file');
}

function bindPersistentQueueImportDropTarget(target) {
  if (!target) return;

  target.addEventListener('dragenter', event => {
    if (!isFileTransfer(event.dataTransfer)) return;
    event.preventDefault();
    event.stopPropagation();
    queueDragDepth++;
    target.classList.add('queueFileDragOver');
    if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
  });

  target.addEventListener('dragover', event => {
    if (!isFileTransfer(event.dataTransfer)) return;
    event.preventDefault();
    event.stopPropagation();
    target.classList.add('queueFileDragOver');
    if (event.dataTransfer) event.dataTransfer.dropEffect = 'copy';
  });

  target.addEventListener('dragleave', event => {
    if (!isFileTransfer(event.dataTransfer)) return;
    event.preventDefault();
    event.stopPropagation();
    queueDragDepth = Math.max(0, queueDragDepth - 1);
    if (queueDragDepth === 0) target.classList.remove('queueFileDragOver');
  });

  target.addEventListener('drop', event => {
    if (!isFileTransfer(event.dataTransfer)) return;
    event.preventDefault();
    event.stopPropagation();
    queueDragDepth = 0;
    target.classList.remove('queueFileDragOver');
    if (!selectReplayFiles(event.dataTransfer?.files)) {
      showToast('Drop replay files');
      return;
    }

    runAction(importSelectedReplays);
  });
}

bindPersistentQueueImportDropTarget(queueDrop);
bindCollectionImportDropTarget(collectionDropTarget);

document.getElementById('uploadReplays').addEventListener('click', () => runAction(importSelectedReplays));
document.getElementById('importReplayReference').addEventListener('click', () => runAction(importReplayReference));

async function importSelectedReplays() {
  if (!selectedReplayFiles.length) {
    showToast('Choose replay files first');
    return;
  }

  const form = new FormData();
  for (const file of selectedReplayFiles) form.append('files', file);
  const response = await fetch('/api/replays/import', { method: 'POST', body: form });
  if (!response.ok) throw new Error(await response.text());
  const result = await response.json();
  state = result.state;
  clearReplaySelection();
  render();
  showToast(`Imported ${result.count} replay${result.count === 1 ? '' : 's'}`);
}

async function importSelectedReplaysToCollection() {
  const collectionId = getSelectedCollectionId();
  if (!collectionId) {
    showToast('Create or select a collection first');
    return;
  }

  if (!selectedCollectionReplayFiles.length) {
    showToast('Choose replay files first');
    return;
  }

  const form = new FormData();
  for (const file of selectedCollectionReplayFiles) form.append('files', file);
  const response = await fetch(`/api/collections/${encodeURIComponent(collectionId)}/import`, {
    method: 'POST',
    body: form
  });
  if (!response.ok) throw new Error(await response.text());
  const result = await response.json();
  state = result.state;
  selectedCollectionId = result.collection?.id || collectionId;
  clearCollectionReplaySelection();
  render();
  const skipped = Number(result.skippedCount) || 0;
  showToast(`Added ${result.importedCount || 0} replay${result.importedCount === 1 ? '' : 's'} to collection${skipped ? `, ${skipped} skipped` : ''}`);
}

async function importReplayReferenceToCollection() {
  const collectionId = getSelectedCollectionId();
  if (!collectionId) {
    showToast('Create or select a collection first');
    return;
  }

  const input = document.getElementById('collectionPageReplayReferenceInput');
  const value = (input?.value || '').trim();
  if (!value) {
    showToast('Paste a replay link first');
    return;
  }

  const references = value
    .split(/\r?\n|,/)
    .map(item => item.trim())
    .filter(Boolean);
  const response = await fetch(`/api/collections/${encodeURIComponent(collectionId)}/import-references`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ references })
  });
  if (!response.ok) throw new Error(await response.text());
  const result = await response.json();
  state = result.state;
  selectedCollectionId = result.collection?.id || collectionId;
  if (input) input.value = '';
  render();
  const skipped = Number(result.skippedCount) || 0;
  showToast(`Added ${result.importedCount || 0} replay${result.importedCount === 1 ? '' : 's'} to collection${skipped ? `, ${skipped} skipped` : ''}`);
}

function filterReplayFiles(files) {
  return files.filter(file => {
    const name = file.name.toLowerCase();
    return name.endsWith('.bsor') || name.endsWith('.dat');
  });
}

async function importReplayReference() {
  const input = document.getElementById('replayReferenceInput');
  const value = (input?.value || '').trim();
  if (!value) {
    showToast('Paste a replay link first');
    return;
  }

  const references = value
    .split(/\r?\n|,/)
    .map(item => item.trim())
    .filter(Boolean);
  const response = await fetch('/api/replays/import-references', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ references })
  });
  if (!response.ok) throw new Error(await response.text());
  const result = await response.json();
  state = result.state;
  if (input) input.value = '';
  render();
  showToast(`Imported ${result.count} replay${result.count === 1 ? '' : 's'}`);
}

function selectReplayFiles(files) {
  selectedReplayFiles = filterReplayFiles(Array.from(files || []));
  updateReplayFileSelection();
  return selectedReplayFiles.length;
}

function selectCollectionReplayFiles(files) {
  selectedCollectionReplayFiles = filterReplayFiles(Array.from(files || []));
  updateCollectionReplayFileSelection();
  return selectedCollectionReplayFiles.length;
}

function clearReplaySelection() {
  selectedReplayFiles = [];
  replayFileInput.value = '';
  queueReplayFileInput.value = '';
  updateReplayFileSelection();
}

function clearCollectionReplaySelection() {
  selectedCollectionReplayFiles = [];
  if (collectionReplayFileInput) collectionReplayFileInput.value = '';
  updateCollectionReplayFileSelection();
}

function updateReplayFileSelection() {
  const selection = document.getElementById('fileSelection');
  if (!selectedReplayFiles.length) {
    selection.textContent = 'No files selected';
    return;
  }

  selection.textContent = selectedReplayFiles.length === 1
    ? selectedReplayFiles[0].name
    : `${selectedReplayFiles.length} files selected`;
}

function updateCollectionReplayFileSelection() {
  const selection = document.getElementById('collectionFileSelection');
  if (!selection) return;

  if (!selectedCollectionReplayFiles.length) {
    selection.textContent = getSelectedCollectionId()
      ? 'Drop .bsor or .dat files here'
      : 'Create or select a collection first';
    return;
  }

  selection.textContent = selectedCollectionReplayFiles.length === 1
    ? selectedCollectionReplayFiles[0].name
    : `${selectedCollectionReplayFiles.length} files selected`;
}

document.getElementById('clearQueue').addEventListener('click', () => runAction(async () => {
  await postJson('/api/queue/clear');
  showToast('Queue cleared');
}));

getRecordingNameFormatElements().forEach(select => {
  select.addEventListener('change', event => {
    syncRecordingNameFormatControls(event.target);
  });
});
syncRecordingNameFormatControls();

document.getElementById('renameUsedQueueRecordings')?.addEventListener('click', () => runAction(() => openRecordingRenameModal('queue')));

document.getElementById('saveCollection')?.addEventListener('click', () => runAction(saveCurrentCollection));
document.getElementById('loadCollection')?.addEventListener('click', () => runAction(loadSelectedCollection));
document.getElementById('exportCollectionCards')?.addEventListener('click', () => runAction(openCollectionMapCardExportModal));
document.getElementById('collectionPageCreate')?.addEventListener('click', () => runAction(createEmptyCollection));
document.getElementById('collectionPageSave')?.addEventListener('click', () => runAction(saveCurrentCollection));
document.getElementById('collectionPageLoad')?.addEventListener('click', () => runAction(loadSelectedCollection));
document.getElementById('collectionPageRename')?.addEventListener('click', () => runAction(renameSelectedCollection));
document.getElementById('collectionPageRenameRecordings')?.addEventListener('click', () => runAction(() => openRecordingRenameModal('collection')));
document.getElementById('collectionPageExportList')?.addEventListener('click', () => runAction(() => openCollectionReplayListExportModal(true)));
document.getElementById('collectionPageExportCards')?.addEventListener('click', () => runAction(openCollectionMapCardExportModal));
document.getElementById('collectionPageDelete')?.addEventListener('click', () => runAction(deleteSelectedCollection));
document.getElementById('collectionPageAddReplays')?.addEventListener('click', openCollectionReplayPicker);
document.getElementById('collectionPageImportFiles')?.addEventListener('click', openCollectionReplayPicker);
document.getElementById('collectionPageImportLink')?.addEventListener('click', () => runAction(importReplayReferenceToCollection));
getCollectionSelectElements().forEach(select => {
  select.addEventListener('change', event => {
    selectedCollectionId = event.target?.value || null;
    renderCollectionControls();
    renderCollectionsPage();
  });
});

document.getElementById('requeueAll')?.addEventListener('click', () => runAction(async () => {
  const count = (state.queue || []).filter(isRequeueableQueueItem).length;
  if (!count) {
    showToast('No replays to requeue');
    return;
  }

  await postJson('/api/queue/requeue-all');
  showToast(`Requeued ${count} replay${count === 1 ? '' : 's'}`);
}));

document.getElementById('exportQueue')?.addEventListener('click', () => runAction(async () => {
  await openQueueExportModal(true);
}));

document.getElementById('queueExportFormat')?.addEventListener('change', () => {
  refreshQueueExportText();
  selectQueueExportText();
});

document.getElementById('copyQueueExport')?.addEventListener('click', () => runAction(copyQueueExportText));
document.getElementById('selectQueueExport')?.addEventListener('click', selectQueueExportText);
document.getElementById('closeQueueExport')?.addEventListener('click', closeQueueExportModal);
document.getElementById('applyRecordingRename')?.addEventListener('click', () => runAction(applyRecordingRenameSelection));
document.getElementById('cancelRecordingRename')?.addEventListener('click', closeRecordingRenameModal);
document.getElementById('closeRecordingRename')?.addEventListener('click', closeRecordingRenameModal);
document.getElementById('mapCardDefaultCategory')?.addEventListener('change', applyMapCardDefaultCategory);
document.getElementById('mapCardCategoriesEnabled')?.addEventListener('change', toggleMapCardCategories);
document.getElementById('saveMapCardCategories')?.addEventListener('click', () => runAction(saveMapCardCategories));
document.getElementById('downloadMapCardGrid')?.addEventListener('click', () => runAction(downloadMapCardGrid));
document.getElementById('downloadMapCards')?.addEventListener('click', () => runAction(downloadMapCards));
document.getElementById('closeMapCardExport')?.addEventListener('click', closeMapCardExportModal);
document.getElementById('queueExportModal')?.addEventListener('click', event => {
  if (event.target?.id === 'queueExportModal') closeQueueExportModal();
});
document.getElementById('recordingRenameModal')?.addEventListener('click', event => {
  if (event.target?.id === 'recordingRenameModal') closeRecordingRenameModal();
});
document.getElementById('mapCardExportModal')?.addEventListener('click', event => {
  if (event.target?.id === 'mapCardExportModal') closeMapCardExportModal();
});

document.addEventListener('keydown', event => {
  if (event.key === 'Escape' && !document.getElementById('queueExportModal')?.hidden) {
    closeQueueExportModal();
  }

  if (event.key === 'Escape' && !document.getElementById('recordingRenameModal')?.hidden) {
    closeRecordingRenameModal();
  }

  if (event.key === 'Escape' && !document.getElementById('mapCardExportModal')?.hidden) {
    closeMapCardExportModal();
  }
});

document.getElementById('launchGames').addEventListener('click', () => runAction(async () => {
  if (getKnownGameInstances().length > 0) {
    await postJson('/api/instances/quit');
    showToast('Stop requested for all recorders');
    return;
  }

  await postJson('/api/instances/launch');
  showToast('Game launch requested');
}));

document.getElementById('closeGamesAfterQueue').addEventListener('click', () => runAction(async () => {
  const closeRequested = Boolean(state.run?.closeGamesWhenFinishedRequested);
  const runActive = Boolean(state.run?.isRunning || state.run?.cancellationRequested);
  const hasPendingQueue = (state.queue || []).some(item => isPendingStatus(item.status));
  if (closeRequested || runActive || hasPendingQueue) {
    await postJson('/api/run/close-games-when-finished', { enabled: !closeRequested });
    showToast(closeRequested ? 'Close after queue canceled' : 'Games will close after this queue');
    return;
  }

  await postJson('/api/instances/quit');
  showToast('Close requested for all games');
}));

document.getElementById('decreaseActiveInstances')?.addEventListener('click', () => changeActiveInstanceCount(-1));
document.getElementById('increaseActiveInstances')?.addEventListener('click', () => changeActiveInstanceCount(1));

document.getElementById('checkBaseline').addEventListener('click', () => runAction(async () => {
  await postJson('/api/instances/baseline/check');
  showToast(`Baseline ${state.instanceBaseline?.status || 'checked'}`);
}));

document.getElementById('startRun').addEventListener('click', () => runAction(async () => {
  await postJson('/api/run/start', { collectionName: getCollectionNameInputValue() });
  showToast('Queue started');
}));

document.getElementById('stopRun').addEventListener('click', () => runAction(async () => {
  await postJson('/api/run/stop');
  showToast('Run stopped');
}));

document.getElementById('startBenchmark')?.addEventListener('click', () => runAction(async () => {
  const concurrencyLevels = getSelectedBenchmarkConcurrencies();
  if (!concurrencyLevels.length) {
    throw new Error('Select at least one benchmark pass.');
  }

  await postJson('/api/benchmark/start', { concurrencyLevels });
  showToast('Benchmark started');
}));

document.getElementById('stopBenchmark')?.addEventListener('click', () => runAction(async () => {
  await postJson('/api/benchmark/stop');
  showToast('Benchmark stop requested');
}));

document.getElementById('benchmarkPassSelector')?.addEventListener('change', event => {
  const input = event.target?.closest?.('[data-benchmark-concurrency]');
  if (!input) return;

  const level = Number(input.dataset.benchmarkConcurrency);
  if (!Number.isInteger(level)) return;

  const selected = new Set(getSelectedBenchmarkConcurrencies());
  if (input.checked) {
    selected.add(level);
  } else {
    selected.delete(level);
  }

  selectedBenchmarkConcurrencies = Array.from(selected).sort((a, b) => a - b);
  renderBenchmark();
});

async function runAction(action) {
  try {
    await action();
  } catch (error) {
    if (shutdownModalVisible) return;
    showToast(readableError(error));
  }
}

async function launchInstance(index) {
  await runAction(async () => {
    await postJson(`/api/instances/${index}/launch`);
    const instance = state.instances.find(item => item.index === index);
    const status = instance?.gameLaunchStatus || 'requested';
    showToast(`${instance?.name || 'Instance'} launch ${status.toLowerCase()}`);
  });
}

async function quitInstance(index) {
  await runAction(async () => {
    const instance = state.instances.find(item => item.index === index);
    const name = formatInstanceDisplayName(instance, index);
    await postJson(`/api/instances/${index}/quit`);
    showToast(`${name} quit requested`);
  });
}

async function changeActiveInstanceCount(delta) {
  const current = getDisplayedActiveInstanceCount();
  const target = Math.max(
    minManagedInstanceCount,
    Math.min(getActiveInstanceLimit(), current + delta));
  if (target === current) return;
  await runAction(() => setActiveRecordingInstanceCount(target));
}

async function setActiveRecordingInstanceCount(count) {
  if (activeInstanceCountActionPending || areRunPlanWorkerControlsLocked()) return;

  const target = Math.max(
    minManagedInstanceCount,
    Math.min(getActiveInstanceLimit(), Number(count) || minManagedInstanceCount));
  if (target === getDisplayedActiveInstanceCount() && pendingActiveInstanceCount == null) return;

  activeInstanceCountActionPending = true;
  runPlanWorkerActionPending = true;
  pendingActiveInstanceCount = target;
  lockRunPlanWorkerControls(900);
  render();
  try {
    await postJson('/api/instances/active-count', { count: target });
    showToast(`Active recording instances: ${getDisplayedActiveInstanceCount()}`);
  } finally {
    pendingActiveInstanceCount = null;
    activeInstanceCountActionPending = false;
    runPlanWorkerActionPending = false;
    lockRunPlanWorkerControls(700);
    render();
  }
}

function formatSeconds(value) {
  const total = Math.max(0, Math.round(value || 0));
  const minutes = Math.floor(total / 60);
  const seconds = String(total % 60).padStart(2, '0');
  return `${minutes}:${seconds}`;
}

function formatFinalizationSeconds(value) {
  const seconds = Number(value);
  if (!Number.isFinite(seconds) || seconds < 0) return '';
  return `${seconds < 10 ? seconds.toFixed(1) : Math.round(seconds).toString()}s`;
}

function formatHeartbeat(value) {
  if (!value) return 'No heartbeat';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return 'No heartbeat';
  const elapsedSeconds = Math.max(0, Math.round((Date.now() - date.getTime()) / 1000));
  if (elapsedSeconds < 2) return 'Just now';
  if (elapsedSeconds < 60) return `${elapsedSeconds}s ago`;
  return `${Math.round(elapsedSeconds / 60)}m ago`;
}

function formatClock(date) {
  return date.toLocaleTimeString([], { hour: 'numeric', minute: '2-digit', second: '2-digit' });
}

function formatBitrate(value) {
  const kbps = Math.max(0, Number(value) || 0);
  if (!kbps) return 'auto bitrate';
  if (kbps < 1000) return `${kbps} kbps`;
  const mbps = kbps / 1000;
  return `${Number.isInteger(mbps) ? mbps.toFixed(0) : mbps.toFixed(1)} Mbps`;
}

function formatContainer(value) {
  const text = String(value ?? 'mkv').trim().toUpperCase();
  return text === 'MP4' ? 'MP4' : 'MKV';
}

function formatMonitorSummary(index) {
  const normalized = Number(index);
  const display = (displayInfo?.displays || []).find(item => Number(item.index) === normalized);
  if (!display) return Number.isFinite(normalized) ? `Monitor ${normalized + 1}` : 'Monitor';

  return buildDetectedMonitorLabel(display);
}

function formatAudioMode(settings) {
  const mode = settings?.audioMode || 'ProcessLoopback';
  if (sameStatus(mode, 'ProcessLoopback')) {
    const levelMode = settings.audioLevelMode || 'Off';
    const levelUnit = sameStatus(levelMode, 'Loudness') ? ' LUFS' : ' dB';
    const level = sameStatus(levelMode, 'Off')
      ? ''
      : ` ${formatSignedNumber(settings.audioTargetLevelDb ?? -12)}${levelUnit}`;
    return 'Loopback' + level;
  }

  return settings?.requireAudioForRun === false ? 'Audio off' : 'Audio off required';
}

function formatInstanceAudioMode(audioEnabled) {
  const settings = state?.settings || {};
  const mode = settings.audioMode || 'ProcessLoopback';
  return audioEnabled && settings.requireAudioForRun !== false && sameStatus(mode, 'ProcessLoopback')
    ? 'Process Loopback'
    : 'Disabled';
}

function formatGamePresentationSync(instance) {
  if (!instance?.workerId) return 'Game settings pending';

  const targetVersion = Number(state?.settings?.gamePresentationSettingsVersion) || 0;
  const appliedVersion = Number(instance.appliedGamePresentationSettingsVersion) || 0;
  const status = instance.gamePresentationSyncStatus || (appliedVersion > 0 ? 'Applied' : 'Pending');
  if (sameStatus(status, 'Failed')) {
    return `Game settings failed: ${instance.gamePresentationSyncError || 'unknown error'}`;
  }

  if (targetVersion > 0 && appliedVersion === targetVersion) {
    return `Game settings v${appliedVersion}`;
  }

  return appliedVersion > 0 && targetVersion > 0
    ? `Game settings ${status} v${appliedVersion}/${targetVersion}`
    : `Game settings ${status}`;
}

function formatSignedNumber(value) {
  const number = Number(value) || 0;
  return number > 0 ? `+${number}` : String(number);
}

function formatNumber(value) {
  const number = Number(value);
  if (!Number.isFinite(number)) return '0';
  return number.toFixed(3).replace(/\.?0+$/, '');
}

function formatFps(value) {
  const number = Number(value);
  if (!Number.isFinite(number) || number <= 0) return '-';
  return `${number.toFixed(1).replace(/\.0$/, '')} FPS`;
}

function shortId(value) {
  const text = String(value ?? '');
  return text.length <= 16 ? text : `${text.slice(0, 16)}...`;
}

function shortFileText(value) {
  const text = String(value ?? '');
  if (!text) return '-';
  const fileName = text.split(/[\\/]/).filter(Boolean).pop() || text;
  return fileName.length <= 24 ? fileName : `${fileName.slice(0, 21)}...`;
}

function initials(value) {
  const words = String(value ?? 'Replay')
    .split(/[^a-z0-9]+/i)
    .filter(Boolean)
    .slice(0, 2);
  return (words.map(word => word[0]).join('') || 'R').toUpperCase();
}

function isPendingStatus(status) {
  const text = String(status ?? '').toLowerCase();
  return text === 'queued' || text === 'assigned' || text === 'recording';
}

function isActiveStatus(status) {
  const text = String(status ?? '').toLowerCase();
  return text === 'assigned' || text === 'recording' || text === 'finalizing';
}

function isRequeueableQueueItem(item) {
  return !isActiveStatus(item?.status) && !sameStatus(item?.status, 'Queued');
}

function sameStatus(left, right) {
  return String(left ?? '').toLowerCase() === String(right ?? '').toLowerCase();
}

function runPlanItemTone(item) {
  const status = String(item?.status ?? '').toLowerCase();
  if (Boolean(item?.error) || /fail|error/.test(status)) return 'failed';
  if (sameStatus(item?.status, 'Completed')) return 'complete';
  if (isActiveStatus(item?.status)) return 'active';
  if (Boolean(item?.warning) || isMapIssueStatus(item?.mapStatus) || isSyncOutOfRange(item)) return 'warning';
  if (sameStatus(item?.status, 'Queued') || isPendingStatus(item?.status)) return 'queued';
  return 'idle';
}

function isMapIssueStatus(status) {
  const text = String(status ?? '').toLowerCase();
  return text === 'missing' || /fail|error|issue/.test(text);
}

function buildRunIssue() {
  const failedReplay = (state.queue || []).find(item => item.error);
  if (failedReplay) {
    const label = failedReplay.songName || failedReplay.fileName || `Replay #${failedReplay.sequenceNumber}`;
    return `#${failedReplay.sequenceNumber} ${label}: ${failedReplay.error}`;
  }

  const failedInstance = (state.instances || []).find(instance => instance.gameLaunchError || instance.audioRoutingError);
  if (failedInstance) {
    const reason = failedInstance.gameLaunchError || failedInstance.audioRoutingError;
    return `${failedInstance.name || `Instance ${Number(failedInstance.index) + 1}`}: ${reason}`;
  }

  const runStatus = String(state.run?.status || '');
  const reason = state.run?.cancellationReason;
  if (reason && /error|fail|stop/i.test(runStatus)) {
    return reason;
  }

  if (/error|fail/i.test(runStatus)) {
    return 'Run stopped before the queue reported a replay-level reason.';
  }

  return '';
}

function hasRunIssue() {
  if ((state.queue || []).some(item => item.error)) return true;
  if ((state.instances || []).some(instance => instance.gameLaunchError || instance.audioRoutingError)) return true;
  return /error|fail/i.test(String(state.run?.status || ''));
}

function statusClass(status) {
  const normalized = String(status ?? 'idle')
    .trim()
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-|-$/g, '');
  return `status-${normalized || 'idle'}`;
}

function readableError(error) {
  const text = error instanceof Error ? error.message : String(error);
  try {
    const parsed = JSON.parse(text);
    return parsed.error || text;
  } catch {
    return text;
  }
}

function showToast(message) {
  if (shutdownModalVisible) return;
  const toast = document.getElementById('toast');
  toast.textContent = message;
  toast.classList.add('visible');
  clearTimeout(toastTimeout);
  toastTimeout = setTimeout(() => toast.classList.remove('visible'), 3500);
}

function showShutdownModal() {
  shutdownModalVisible = true;
  document.title = 'Replay Recorder Off';
  document.body.classList.add('shutdownLocked');
  const toast = document.getElementById('toast');
  toast?.classList.remove('visible');
  clearTimeout(toastTimeout);

  const modal = document.getElementById('shutdownModal');
  if (modal) {
    modal.hidden = false;
    modal.focus?.();
  }
}

function escapeHtml(value) {
  return String(value ?? '')
    .replaceAll('&', '&amp;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;')
    .replaceAll('"', '&quot;')
    .replaceAll("'", '&#039;');
}

activateView(location.hash.replace('#', ''), false);

loadState().catch(error => {
  document.getElementById('runStatus').textContent = readableError(error);
});
loadDisplayInfo().catch(() => {
  displayInfo = { displays: [] };
  renderMonitorOptions();
});
loadColorPresets().catch(() => {
  colorPresetCatalog = { builtIn: [], beatSaber: [], saved: [] };
  renderColorPresetOptions();
});
loadSetupSourcePath().catch(() => {
  setupSourcePathInfo = null;
  renderSetupSourcePath(buildCurrentSettingsPreview());
});

setInterval(() => {
  if (shutdownModalVisible) return;
  if (settingsDirty) return;
  if (editingQueueId) return;
  const activeElement = document.activeElement;
  if (activeElement && (activeElement.tagName === 'INPUT' || activeElement.tagName === 'SELECT' || activeElement.tagName === 'TEXTAREA')) return;
  loadState().catch(() => {});
}, 2500);

setInterval(() => {
  if (shouldTickRunPlanPlayhead()) updateRunPlanPlayhead();
}, 500);

function shouldTickRunPlanPlayhead() {
  if (!document.querySelector('[data-run-plan-playhead]')) return false;
  if (!state?.run?.isRunning && !state?.run?.cancellationRequested) return false;
  if (state.run?.startedAtUtc) return true;
  return (state.queue || []).some(item => isActiveStatus(item.status) && item.assignedAtUtc);
}
