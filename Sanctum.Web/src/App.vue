<script setup>
import { ref, computed, onMounted, onUnmounted } from 'vue';
import * as signalR from '@microsoft/signalr';
import QRCode from 'qrcode';
import { apiFetch } from './services/api.js';

const activeTab = ref('today');
const liveConnected = ref(false);
let hubConnection = null;

const me = ref(null);
const tasks = ref([]);
const dailyStars = ref({ today: null, history: [] });
const starNote = ref('');
const consent = ref([]);
const rewards = ref([]);
const transactions = ref([]);
const corrections = ref([]);
const scenes = ref([]);
const rules = ref([]);
const aftercare = ref([]);
const journal = ref([]);

const devEmail = ref(localStorage.getItem('sanctum_dev_email') || 'dom@kinkyfairytales.ca');

// Aftercare CRUD State
const editingAftercareMode = ref(false);
const newAftercareCategory = ref('Comfort');
const newAftercareLabel = ref('');
const editingAftercareId = ref(null);
const draftAftercare = ref({});

// Consent Tab State
const consentViewMode = ref('mine');
const detailedConsentMode = ref(true);
const consentFilter = ref('All');
const consentSearch = ref('');
const compareFilter = ref('all');
const expandedConsentId = ref(null);
const draftEdit = ref({});

const newConsentCategory = ref('Protocol');
const newConsentActivity = ref('');
const newConsentLevel = ref(0);
const newConsentTopExp = ref('');
const newConsentBotExp = ref('');

// Tasks & Rules State
const newTaskTitle = ref('');
const newTaskFreq = ref(1);
const newTaskPoints = ref(15);
const newTaskRequiresApproval = ref(false);

const newRuleCategory = ref('Communication');
const newRuleTitle = ref('');
const newRuleDetails = ref('');
const editingRuleId = ref(null);
const draftRule = ref({});

// Rewards & Jar CRUD State
const newRewardTitle = ref('');
const newRewardDesc = ref('');
const newRewardCost = ref(50);
const newRewardRepeatable = ref(true);
const editingRewardId = ref(null);
const draftReward = ref({});

const showJarManager = ref(false);
const newJarTitle = ref('');
const newJarReason = ref('');
const editingJarId = ref(null);
const draftJar = ref({});

// Scenes & Journal State
const newSceneTitle = ref('');
const newSceneGoal = ref('');
const newSceneSafety = ref(true);
const selectedConsentForScene = ref({});
const newGearName = ref({});
const journalContent = ref('');
const journalMood = ref('Grounded');
const journalPrivate = ref(false);
const draftComments = ref({});
const spaceNameDraft = ref('');
const memberEdits = ref({});
const deviceName = ref('Galaxy Z Flip 8');
const pairingQrDataUrl = ref('');

const statusMap = { 0: 'GREEN', 1: 'YELLOW', 2: 'RED' };
const sceneStatusMap = { 0: 'Draft', 1: 'Scheduled', 2: 'Active', 3: 'Completed', 4: 'Cancelled' };
const consentLevels = ['Yes', 'Maybe', 'No', 'HardLimit'];

const starTiers = [
  { tier: 0, label: 'Nothing', sub: 'Existed', pts: 0, icon: '—', activeClass: 'bg-zinc-700 text-white border-zinc-500' },
  { tier: 1, label: 'Silver Star', sub: 'Basics', pts: 5, icon: '🥈', activeClass: 'bg-slate-400/25 text-slate-200 border-slate-300' },
  { tier: 2, label: 'Gold Star', sub: 'Thrived', pts: 15, icon: '⭐', activeClass: 'bg-amber-500/25 text-amber-200 border-amber-400' },
  { tier: 3, label: 'Super Gold Star', sub: 'Amazing', pts: 25, icon: '🌟', activeClass: 'bg-yellow-400/30 text-yellow-200 border-yellow-300 shadow-sm shadow-amber-500/20' }
];

const interestOptions = [
  { code: '', label: '—' },
  { code: '!', label: '! — Need It' },
  { code: '5', label: '5 — Love It' },
  { code: '4', label: '4 — Like It' },
  { code: '3', label: '3 — Interested' },
  { code: '2', label: '2 — Willing for Partner' },
  { code: '?', label: '? — Unsure' },
  { code: '1', label: '1 — Soft Limit' },
  { code: 'X', label: 'X — Hard Limit' }
];

const expOptions = [
  { code: '', label: '—' },
  { code: '0', label: '0 — None' },
  { code: '1', label: '1 — Little' },
  { code: '2', label: '2 — Some' },
  { code: '3', label: '3 — Moderate' },
  { code: '4', label: '4 — Experienced' },
  { code: '5', label: '5 — Very Exp' },
  { code: '?', label: '? — Unsure' }
];

const getStarMeta = (tier) => starTiers.find(s => s.tier === tier) || starTiers[0];

const loadAll = async () => {
  try {
    me.value = await apiFetch('/api/me');
    const [t, ds, c, r, tx, co, sc, ru, ac, j] = await Promise.all([
      apiFetch('/api/tasks'),
      apiFetch('/api/daily-stars'),
      apiFetch('/api/consent'),
      apiFetch('/api/rewards'),
      apiFetch('/api/transactions'),
      apiFetch('/api/corrections'),
      apiFetch('/api/scenes'),
      apiFetch('/api/rules'),
      apiFetch('/api/aftercare'),
      apiFetch('/api/journal')
    ]);
    tasks.value = t;
    dailyStars.value = ds;
    if (ds?.today?.note && !starNote.value) starNote.value = ds.today.note;
    consent.value = c;
    rewards.value = r;
    transactions.value = tx;
    corrections.value = co;
    scenes.value = sc;
    rules.value = ru;
    aftercare.value = ac;
    journal.value = j;
    if (me.value?.space?.name) spaceNameDraft.value = me.value.space.name;
    if (me.value?.members) {
      const map = {};
      for (const m of me.value.members) {
        map[m.id] = {
          email: m.email,
          roleTitle: m.roleTitle,
          canManageRules: m.canManageRules,
          canAssignTasks: m.canAssignTasks,
          canApproveRewards: m.canApproveRewards,
          canAssignCorrections: m.canAssignCorrections
        };
      }
      memberEdits.value = map;
    }
  } catch (e) {
    console.error('Load failed:', e);
  }
};

const connectSignalR = async () => {
  hubConnection = new signalR.HubConnectionBuilder()
    .withUrl('/hubs/sanctum', {
      headers: { 'X-Dev-User-Email': devEmail.value }
    })
    .withAutomaticReconnect()
    .build();

  hubConnection.on('StateChanged', () => loadAll());
  hubConnection.on('SafetyUpdated', () => loadAll());
  hubConnection.onreconnected(() => { liveConnected.value = true; loadAll(); });
  hubConnection.onclose(() => { liveConnected.value = false; });

  try {
    await hubConnection.start();
    liveConnected.value = true;
  } catch (err) {
    console.warn('SignalR connection pending:', err);
  }
};

const switchDevRole = async (email) => {
  devEmail.value = email;
  localStorage.setItem('sanctum_dev_email', email);
  expandedConsentId.value = null;
  editingRuleId.value = null;
  editingRewardId.value = null;
  editingJarId.value = null;
  await loadAll();
};

const setSafety = async (status) => {
  const note = status > 0 ? prompt('Optional note for your partner:') : null;
  await apiFetch('/api/safety', { method: 'POST', body: JSON.stringify({ status, note }) });
  await loadAll();
};

const awardDailyStar = async (tier) => {
  try {
    await apiFetch('/api/daily-stars', {
      method: 'POST',
      body: JSON.stringify({ tier, note: starNote.value })
    });
    await loadAll();
  } catch (e) {
    alert(e.message || 'Could not save star rating.');
  }
};

// Tasks
const completeTask = async (id) => {
  try {
    await apiFetch(`/api/tasks/${id}/complete`, { method: 'POST' });
    await loadAll();
  } catch (e) {
    alert(e.message);
  }
};

const approveTask = async (id) => {
  await apiFetch(`/api/tasks/${id}/approve`, { method: 'POST' });
  await loadAll();
};

const rejectTask = async (id) => {
  await apiFetch(`/api/tasks/${id}/reject`, { method: 'POST' });
  await loadAll();
};

const deleteTask = async (id) => {
  if (!confirm('Delete this task?')) return;
  await apiFetch(`/api/tasks/${id}`, { method: 'DELETE' });
  await loadAll();
};

const createTask = async () => {
  if (!newTaskTitle.value.trim()) return;
  try {
    await apiFetch('/api/tasks', {
      method: 'POST',
      body: JSON.stringify({
        title: newTaskTitle.value,
        frequency: Number(newTaskFreq.value),
        pointValue: Number(newTaskPoints.value),
        requiresDomApproval: newTaskRequiresApproval.value
      })
    });
    newTaskTitle.value = '';
    newTaskRequiresApproval.value = false;
    await loadAll();
  } catch (e) {
    alert(e.message || 'Failed to create task.');
  }
};

// Aftercare CRUD
const toggleAftercare = async (id) => {
  await apiFetch(`/api/aftercare/${id}/toggle`, { method: 'POST' });
  await loadAll();
};

const createAftercare = async () => {
  if (!newAftercareLabel.value.trim()) return;
  await apiFetch('/api/aftercare', {
    method: 'POST',
    body: JSON.stringify({ category: newAftercareCategory.value, label: newAftercareLabel.value })
  });
  newAftercareLabel.value = '';
  await loadAll();
};

const startEditAftercare = (item) => {
  if (editingAftercareId.value === item.id) {
    editingAftercareId.value = null;
    return;
  }
  editingAftercareId.value = item.id;
  draftAftercare.value[item.id] = { category: item.category, label: item.label };
};

const saveAftercare = async (id) => {
  const da = draftAftercare.value[id];
  if (!da) return;
  await apiFetch(`/api/aftercare/${id}`, {
    method: 'PUT',
    body: JSON.stringify(da)
  });
  editingAftercareId.value = null;
  await loadAll();
};

const deleteAftercare = async (id) => {
  if (!confirm('Delete this aftercare chip?')) return;
  await apiFetch(`/api/aftercare/${id}`, { method: 'DELETE' });
  await loadAll();
};

// Standing Rules CRUD
const createRule = async () => {
  if (!newRuleTitle.value.trim()) return;
  try {
    await apiFetch('/api/rules', {
      method: 'POST',
      body: JSON.stringify({
        category: newRuleCategory.value,
        title: newRuleTitle.value,
        details: newRuleDetails.value,
        isActive: true
      })
    });
    newRuleTitle.value = '';
    newRuleDetails.value = '';
    await loadAll();
  } catch (e) {
    alert(e.message || 'Failed to add rule.');
  }
};

const startEditRule = (rule) => {
  if (editingRuleId.value === rule.id) {
    editingRuleId.value = null;
    return;
  }
  editingRuleId.value = rule.id;
  draftRule.value[rule.id] = {
    category: rule.category,
    title: rule.title,
    details: rule.details,
    isActive: rule.isActive
  };
};

const saveRule = async (id) => {
  const dr = draftRule.value[id];
  if (!dr) return;
  await apiFetch(`/api/rules/${id}`, {
    method: 'PUT',
    body: JSON.stringify(dr)
  });
  editingRuleId.value = null;
  await loadAll();
};

const toggleRuleActive = async (rule) => {
  await apiFetch(`/api/rules/${rule.id}`, {
    method: 'PUT',
    body: JSON.stringify({
      category: rule.category,
      title: rule.title,
      details: rule.details,
      isActive: !rule.isActive
    })
  });
  await loadAll();
};

const deleteRule = async (id) => {
  if (!confirm('Delete this rule/protocol?')) return;
  await apiFetch(`/api/rules/${id}`, { method: 'DELETE' });
  await loadAll();
};

// Consent Actions
const toggleExpandConsent = (item) => {
  if (expandedConsentId.value === item.id) {
    expandedConsentId.value = null;
    return;
  }
  expandedConsentId.value = item.id;
  draftEdit.value[item.id] = {
    category: item.category,
    activityName: item.activityName,
    notes: item.conditionsOrNotes || '',
    givingInterestCode: item.givingInterestCode || '',
    givingExperienceCode: item.givingExperienceCode || '',
    receivingInterestCode: item.receivingInterestCode || '',
    receivingExperienceCode: item.receivingExperienceCode || ''
  };
};

const updateConsentLevel = async (item, levelIndex) => {
  await apiFetch(`/api/consent/${item.id}`, {
    method: 'PUT',
    body: JSON.stringify({
      category: item.category,
      activityName: item.activityName,
      level: levelIndex,
      notes: item.conditionsOrNotes,
      givingInterestCode: item.givingInterestCode,
      givingExperienceCode: item.givingExperienceCode,
      receivingInterestCode: item.receivingInterestCode,
      receivingExperienceCode: item.receivingExperienceCode
    })
  });
  await loadAll();
};

const saveConsentDetails = async (item) => {
  const ed = draftEdit.value[item.id];
  if (!ed) return;
  await apiFetch(`/api/consent/${item.id}`, {
    method: 'PUT',
    body: JSON.stringify({
      category: ed.category,
      activityName: ed.activityName,
      level: item.level,
      notes: ed.notes,
      givingInterestCode: ed.givingInterestCode,
      givingExperienceCode: ed.givingExperienceCode,
      receivingInterestCode: ed.receivingInterestCode,
      receivingExperienceCode: ed.receivingExperienceCode
    })
  });
  expandedConsentId.value = null;
  await loadAll();
};

const deleteConsentItem = async (item) => {
  if (!confirm(`Delete "${item.activityName}" for both partners?`)) return;
  await apiFetch(`/api/consent/${item.id}`, { method: 'DELETE' });
  expandedConsentId.value = null;
  await loadAll();
};

const addManualConsent = async () => {
  if (!newConsentActivity.value.trim()) return;
  await apiFetch('/api/consent', {
    method: 'POST',
    body: JSON.stringify({
      category: newConsentCategory.value,
      activityName: newConsentActivity.value,
      level: Number(newConsentLevel.value),
      notes: '',
      givingExperienceCode: newConsentTopExp.value,
      receivingExperienceCode: newConsentBotExp.value
    })
  });
  newConsentActivity.value = '';
  newConsentTopExp.value = '';
  newConsentBotExp.value = '';
  await loadAll();
};

const handleCsvUpload = async (event) => {
  const file = event.target.files?.[0];
  if (!file) return;
  const text = await file.text();
  const res = await apiFetch('/api/consent/import-csv', {
    method: 'POST',
    body: JSON.stringify({ csvContent: text })
  });
  alert(`Imported / updated ${res.importedCount} checklist items.`);
  await loadAll();
};

// Rewards Store CRUD & Redemption
const createReward = async () => {
  if (!newRewardTitle.value.trim()) return;
  try {
    await apiFetch('/api/rewards', {
      method: 'POST',
      body: JSON.stringify({
        title: newRewardTitle.value,
        description: newRewardDesc.value,
        pointCost: Number(newRewardCost.value),
        isRepeatable: newRewardRepeatable.value
      })
    });
    newRewardTitle.value = '';
    newRewardDesc.value = '';
    newRewardCost.value = 50;
    newRewardRepeatable.value = true;
    await loadAll();
  } catch (e) {
    alert(e.message || 'Could not add reward.');
  }
};

const startEditReward = (rw) => {
  if (editingRewardId.value === rw.id) {
    editingRewardId.value = null;
    return;
  }
  editingRewardId.value = rw.id;
  draftReward.value[rw.id] = {
    title: rw.title,
    description: rw.description || '',
    pointCost: rw.pointCost,
    isRepeatable: rw.isRepeatable
  };
};

const saveReward = async (id) => {
  const dr = draftReward.value[id];
  if (!dr) return;
  await apiFetch(`/api/rewards/${id}`, {
    method: 'PUT',
    body: JSON.stringify({
      title: dr.title,
      description: dr.description,
      pointCost: Number(dr.pointCost),
      isRepeatable: dr.isRepeatable
    })
  });
  editingRewardId.value = null;
  await loadAll();
};

const deleteReward = async (id) => {
  if (!confirm('Delete this reward?')) return;
  await apiFetch(`/api/rewards/${id}`, { method: 'DELETE' });
  await loadAll();
};

const redeemReward = async (id) => {
  try {
    await apiFetch(`/api/rewards/${id}/redeem`, { method: 'POST' });
    await loadAll();
  } catch (e) {
    alert(e.message);
  }
};

const fulfillReward = async (id) => {
  await apiFetch(`/api/rewards/${id}/fulfill`, { method: 'POST' });
  await loadAll();
};

// Punishment Jar CRUD & Draw
const drawPunishment = async () => {
  try {
    const drawn = await apiFetch('/api/corrections/draw', { method: 'POST' });
    alert(`Drawn from Jar: ${drawn.title}`);
    await loadAll();
  } catch (e) {
    alert(e.message);
  }
};

const addJarOption = async () => {
  if (!newJarTitle.value.trim()) return;
  try {
    await apiFetch('/api/corrections', {
      method: 'POST',
      body: JSON.stringify({ title: newJarTitle.value, reason: newJarReason.value, isPoolOption: true })
    });
    newJarTitle.value = '';
    newJarReason.value = '';
    await loadAll();
  } catch (e) {
    alert(e.message || 'Could not add jar option.');
  }
};

const startEditJar = (item) => {
  if (editingJarId.value === item.id) {
    editingJarId.value = null;
    return;
  }
  editingJarId.value = item.id;
  draftJar.value[item.id] = { title: item.title, reason: item.reason || '' };
};

const saveJarOption = async (id) => {
  const dj = draftJar.value[id];
  if (!dj) return;
  await apiFetch(`/api/corrections/${id}`, {
    method: 'PUT',
    body: JSON.stringify({ title: dj.title, reason: dj.reason, isPoolOption: true })
  });
  editingJarId.value = null;
  await loadAll();
};

const deleteJarOption = async (id) => {
  if (!confirm('Remove this option from the Punishment Jar?')) return;
  await apiFetch(`/api/corrections/${id}`, { method: 'DELETE' });
  await loadAll();
};

const resolveCorrection = async (id) => {
  await apiFetch(`/api/corrections/${id}/resolve`, { method: 'POST' });
  await loadAll();
};

// Scenes & Journal
const createScene = async () => {
  if (!newSceneTitle.value.trim()) return;
  await apiFetch('/api/scenes', {
    method: 'POST',
    body: JSON.stringify({
      title: newSceneTitle.value,
      headspaceGoal: newSceneGoal.value,
      estimatedDurationMinutes: 60,
      includeSafetyEssentials: newSceneSafety.value
    })
  });
  newSceneTitle.value = '';
  newSceneGoal.value = '';
  await loadAll();
};

const addActivityToScene = async (sceneId) => {
  const consentItemId = selectedConsentForScene.value[sceneId];
  if (!consentItemId) return;
  await apiFetch(`/api/scenes/${sceneId}/activities`, {
    method: 'POST',
    body: JSON.stringify({ consentItemId, isWarmup: false })
  });
  selectedConsentForScene.value[sceneId] = '';
  await loadAll();
};

const addGearToScene = async (sceneId) => {
  const itemName = newGearName.value[sceneId];
  if (!itemName?.trim()) return;
  await apiFetch(`/api/scenes/${sceneId}/gear`, {
    method: 'POST',
    body: JSON.stringify({ category: 'Gear', itemName })
  });
  newGearName.value[sceneId] = '';
  await loadAll();
};

const toggleGear = async (gearId) => {
  await apiFetch(`/api/scenes/gear/${gearId}/toggle`, { method: 'POST' });
  await loadAll();
};

const setSceneStatus = async (sceneId, status) => {
  await apiFetch(`/api/scenes/${sceneId}/status`, {
    method: 'POST',
    body: JSON.stringify({ status })
  });
  await loadAll();
};

const postJournal = async (moodOverride = null) => {
  const text = moodOverride ? `Quick check-in: feeling ${moodOverride}.` : journalContent.value;
  if (!text.trim()) return;
  await apiFetch('/api/journal', {
    method: 'POST',
    body: JSON.stringify({
      content: text,
      moodTag: moodOverride || journalMood.value,
      isPrivateToAuthor: moodOverride ? false : journalPrivate.value
    })
  });
  if (!moodOverride) {
    journalContent.value = '';
    journalPrivate.value = false;
  }
  await loadAll();
};

const saveJournalComment = async (entryId) => {
  await apiFetch(`/api/journal/${entryId}/comment`, {
    method: 'POST',
    body: JSON.stringify({ comment: draftComments.value[entryId] ?? '' })
  });
  await loadAll();
};

const deleteJournalEntry = async (entryId) => {
  if (!confirm('Delete this journal entry?')) return;
  await apiFetch(`/api/journal/${entryId}`, { method: 'DELETE' });
  await loadAll();
};

const saveSpaceName = async () => {
  if (!spaceNameDraft.value.trim()) return;
  await apiFetch('/api/space', {
    method: 'PUT',
    body: JSON.stringify({ name: spaceNameDraft.value })
  });
  await loadAll();
};

const saveMemberSettings = async (memberId) => {
  const payload = memberEdits.value[memberId];
  if (!payload) return;
  await apiFetch(`/api/members/${memberId}`, {
    method: 'PUT',
    body: JSON.stringify(payload)
  });
  await loadAll();
};

const generatePairingQr = async () => {
  const session = await apiFetch('/api/devices/pairing-session', {
    method: 'POST',
    body: JSON.stringify({ deviceName: deviceName.value })
  });
  pairingQrDataUrl.value = await QRCode.toDataURL(JSON.stringify(session), { width: 220, margin: 2 });
};

// Computed Queues & Lists
const pendingTasks = computed(() => tasks.value.filter(t => t.isPendingApproval));
const pendingRewards = computed(() => rewards.value.filter(r => r.isPendingFulfillment));
const activeAftercareRequests = computed(() => aftercare.value.filter(a => a.isRequested));

const partnerMember = computed(() => me.value?.members?.find(m => m.id !== me.value?.id) || null);
const myConsent = computed(() => consent.value.filter(c => c.memberId === me.value?.id));
const partnerConsent = computed(() => partnerMember.value ? consent.value.filter(c => c.memberId === partnerMember.value.id) : []);

const activeMemberConsent = computed(() => consentViewMode.value === 'partner' ? partnerConsent.value : myConsent.value);
const hardLimits = computed(() => activeMemberConsent.value.filter(c => c.level === 3));
const categories = computed(() => ['All', ...new Set(consent.value.map(c => c.category))]);

const filteredConsent = computed(() => {
  const q = consentSearch.value.trim().toLowerCase();
  return activeMemberConsent.value.filter(c => {
    const matchesCat = consentFilter.value === 'All' || c.category === consentFilter.value;
    const matchesSearch = !q || c.activityName.toLowerCase().includes(q) || c.category.toLowerCase().includes(q) || (c.conditionsOrNotes && c.conditionsOrNotes.toLowerCase().includes(q));
    return matchesCat && matchesSearch;
  });
});

const comparisonRows = computed(() => {
  const map = new Map();
  for (const item of myConsent.value) {
    const key = `${item.category}|${item.activityName}`.toLowerCase();
    map.set(key, { key, category: item.category, activityName: item.activityName, mine: item, theirs: null });
  }
  for (const item of partnerConsent.value) {
    const key = `${item.category}|${item.activityName}`.toLowerCase();
    if (map.has(key)) map.get(key).theirs = item;
    else map.set(key, { key, category: item.category, activityName: item.activityName, mine: null, theirs: item });
  }

  const q = consentSearch.value.trim().toLowerCase();
  return Array.from(map.values())
    .map(row => {
      const mLvl = row.mine?.level;
      const tLvl = row.theirs?.level;
      let status = 'unmatched';
      if (mLvl === 3 || tLvl === 3) status = 'conflict';
      else if (mLvl === 0 && tLvl === 0) status = 'match';
      else if ((mLvl === 0 || mLvl === 1) && (tLvl === 0 || tLvl === 1)) status = 'discuss';
      return { ...row, status };
    })
    .filter(row => {
      const matchesCat = consentFilter.value === 'All' || row.category === consentFilter.value;
      const matchesSearch = !q || row.activityName.toLowerCase().includes(q) || row.category.toLowerCase().includes(q);
      const matchesComp = compareFilter.value === 'all' || row.status === compareFilter.value;
      return matchesCat && matchesSearch && matchesComp;
    })
    .sort((a, b) => a.category.localeCompare(b.category) || a.activityName.localeCompare(b.activityName));
});

const eligibleSceneConsent = computed(() => myConsent.value.filter(c => c.level === 0 || c.level === 1));
const activeCorrections = computed(() => corrections.value.filter(c => !c.isPoolOption && !c.isResolved));
const jarPool = computed(() => corrections.value.filter(c => c.isPoolOption));

const levelBadgeClass = (lvl) => ({
  0: 'bg-emerald-600/20 text-emerald-300 border-emerald-500/40',
  1: 'bg-amber-500/20 text-amber-300 border-amber-500/40',
  2: 'bg-zinc-800 text-zinc-400 border-zinc-700',
  3: 'bg-rose-600/20 text-rose-300 border-rose-500/40'
}[lvl] || 'bg-zinc-800 text-zinc-400');

const levelLabel = (lvl) => ({ 0: 'Yes', 1: 'Maybe', 2: 'No', 3: 'Hard Limit' }[lvl] || '—');

onMounted(async () => {
  await loadAll();
  await connectSignalR();
});

onUnmounted(() => {
  if (hubConnection) hubConnection.stop();
});
</script>

<template>
  <div class="min-h-screen w-full pb-24">
    <div class="w-full max-w-4xl mx-auto px-4 pt-4">
      <!-- Persistent Global Header -->
      <header v-if="me" class="flex flex-wrap items-center justify-between gap-2 bg-zinc-900/90 border border-zinc-800 rounded-2xl px-4 py-3 mb-5 sticky top-3 z-20 backdrop-blur">
        <div>
          <div class="flex items-center gap-2">
            <span class="font-bold tracking-wide text-rose-400">SANCTUM</span>
            <span class="text-xs px-2 py-0.5 rounded-full bg-zinc-800 text-zinc-200 font-medium">{{ me.roleTitle }}</span>
            <span :title="liveConnected ? 'Live SignalR Sync Active' : 'Sync Offline'" class="w-2 h-2 rounded-full" :class="liveConnected ? 'bg-emerald-400' : 'bg-zinc-600'"></span>
            <div v-if="me.isDevelopment && me.members?.length" class="flex items-center gap-1 ml-1 bg-zinc-950 border border-zinc-800 rounded-lg p-0.5 text-[11px]">
              <button v-for="m in me.members" :key="m.id" @click="switchDevRole(m.email)"
                :class="me.user.email === m.email ? 'bg-rose-600 text-white' : 'text-zinc-400'"
                class="px-1.5 py-0.5 rounded">
                {{ m.roleTitle }}
              </button>
            </div>
          </div>
          <div class="text-xs text-zinc-400 mt-0.5">Balance: <strong class="text-amber-400">{{ me.pointBalance }} pts</strong></div>
        </div>

        <!-- Safeword Status Selector -->
        <div class="flex items-center gap-1.5">
          <button @click="setSafety(0)" :class="me.space.currentSafetyStatus === 0 ? 'bg-emerald-600 text-white' : 'bg-zinc-800 text-zinc-400'" class="px-2.5 py-1 rounded-lg text-xs font-semibold">GREEN</button>
          <button @click="setSafety(1)" :class="me.space.currentSafetyStatus === 1 ? 'bg-amber-500 text-zinc-950' : 'bg-zinc-800 text-zinc-400'" class="px-2.5 py-1 rounded-lg text-xs font-semibold">YELLOW</button>
          <button @click="setSafety(2)" :class="me.space.currentSafetyStatus === 2 ? 'bg-rose-600 text-white' : 'bg-zinc-800 text-zinc-400'" class="px-2.5 py-1 rounded-lg text-xs font-semibold">RED</button>
        </div>
      </header>

      <!-- Safety Alert Banner -->
      <div v-if="me && me.space.currentSafetyStatus > 0" class="mb-5 p-4 rounded-2xl border border-rose-500/40 bg-rose-950/40">
        <div class="font-bold text-rose-300">Active Status: {{ statusMap[me.space.currentSafetyStatus] }}</div>
        <div v-if="me.space.safetyNote" class="text-sm text-zinc-200 mt-1">{{ me.space.safetyNote }}</div>
      </div>

      <!-- Active Aftercare Requests Banner -->
      <div v-if="activeAftercareRequests.length" class="mb-5 p-3.5 rounded-2xl border border-amber-500/40 bg-amber-950/30 flex flex-wrap items-center justify-between gap-2">
        <div class="text-xs font-semibold text-amber-300">Active Aftercare Requests:</div>
        <div class="flex flex-wrap gap-1.5">
          <button v-for="req in activeAftercareRequests" :key="req.id" @click="toggleAftercare(req.id)"
            class="px-2.5 py-1 rounded-lg bg-amber-500/20 border border-amber-500/40 text-amber-200 text-xs">
            {{ req.label }} ✕
          </button>
        </div>
      </div>

      <!-- Dom Approval & Fulfillment Queue Banner -->
      <div v-if="me && (me.canAssignTasks || me.canApproveRewards) && (pendingTasks.length || pendingRewards.length)"
        class="mb-5 p-4 rounded-2xl border border-sky-500/40 bg-sky-950/20 space-y-3">
        <div class="text-xs font-bold text-sky-300 uppercase tracking-wider">Approval & Fulfillment Queue</div>

        <div v-for="pt in pendingTasks" :key="pt.id" class="flex items-center justify-between gap-2 p-2.5 rounded-xl bg-zinc-950 border border-zinc-800 text-xs">
          <div>
            <span class="text-amber-400 font-semibold">[Task +{{ pt.pointValue }} pts]</span> {{ pt.title }}
          </div>
          <div class="flex gap-1.5">
            <button @click="approveTask(pt.id)" class="px-2.5 py-1 rounded-lg bg-emerald-600 hover:bg-emerald-500 text-white font-semibold">Approve</button>
            <button @click="rejectTask(pt.id)" class="px-2.5 py-1 rounded-lg bg-zinc-800 hover:bg-rose-700 text-zinc-300">Reject</button>
          </div>
        </div>

        <div v-for="pr in pendingRewards" :key="pr.id" class="flex items-center justify-between gap-2 p-2.5 rounded-xl bg-zinc-950 border border-zinc-800 text-xs">
          <div>
            <span class="text-rose-400 font-semibold">[Reward Redeemed]</span> {{ pr.title }}
          </div>
          <button @click="fulfillReward(pr.id)" class="px-2.5 py-1 rounded-lg bg-sky-600 hover:bg-sky-500 text-white font-semibold">Mark Fulfilled</button>
        </div>
      </div>

      <!-- TAB 1: TODAY -->
      <section v-if="activeTab === 'today'" class="space-y-5">
        <!-- Daily Star Sticker Rating Card -->
        <div class="bg-zinc-900 border border-zinc-800 rounded-2xl p-4 space-y-3">
          <div class="flex items-center justify-between">
            <div>
              <h2 class="text-sm font-semibold text-amber-400 uppercase tracking-wider">Daily Star Rating</h2>
              <p class="text-xs text-zinc-400">How well she made it through today</p>
            </div>
            <div v-if="dailyStars?.today" class="px-3 py-1 rounded-xl border text-xs font-semibold" :class="getStarMeta(dailyStars.today.tier).activeClass">
              {{ getStarMeta(dailyStars.today.tier).icon }} {{ getStarMeta(dailyStars.today.tier).label }}
            </div>
            <div v-else class="text-xs text-zinc-500">Not rated yet today</div>
          </div>

          <template v-if="me?.canAssignTasks">
            <div class="grid grid-cols-2 sm:grid-cols-4 gap-2">
              <button v-for="st in starTiers" :key="st.tier" @click="awardDailyStar(st.tier)"
                :class="dailyStars?.today?.tier === st.tier ? st.activeClass : 'bg-zinc-950 text-zinc-300 border-zinc-800 hover:border-zinc-700'"
                class="p-2.5 rounded-xl border text-left transition">
                <div class="text-base">{{ st.icon }}</div>
                <div class="text-xs font-semibold mt-0.5">{{ st.label }}</div>
                <div class="text-[11px] text-zinc-400">{{ st.sub }} · +{{ st.pts }} pts</div>
              </button>
            </div>
            <div class="flex gap-2">
              <input v-model="starNote" placeholder="Optional praise or note for today's star..."
                class="flex-1 px-3 py-1.5 rounded-xl bg-zinc-950 border border-zinc-800 text-xs" />
              <button v-if="dailyStars?.today" @click="awardDailyStar(dailyStars.today.tier)"
                class="px-3 py-1.5 rounded-xl bg-zinc-800 hover:bg-zinc-700 text-xs font-semibold">
                Save Note
              </button>
            </div>
          </template>

          <template v-else>
            <div class="p-3 rounded-xl bg-zinc-950 border border-zinc-800 text-xs">
              <div v-if="dailyStars?.today">
                <span class="font-semibold text-zinc-200">Today's Rating:</span>
                <span class="ml-1.5 text-amber-300 font-bold">{{ getStarMeta(dailyStars.today.tier).icon }} {{ getStarMeta(dailyStars.today.tier).label }}</span>
                <span class="text-zinc-400 ml-1">({{ getStarMeta(dailyStars.today.tier).sub }})</span>
                <p v-if="dailyStars.today.note" class="text-zinc-300 mt-1.5 italic">"{{ dailyStars.today.note }}"</p>
              </div>
              <div v-else class="text-zinc-500">Waiting for Dom's end-of-day star rating.</div>
            </div>
          </template>

          <div v-if="dailyStars?.history?.length" class="pt-2 border-t border-zinc-800/80">
            <div class="text-[11px] text-zinc-500 mb-1.5">Recent Sticker Chart</div>
            <div class="flex gap-1.5 overflow-x-auto pb-1">
              <div v-for="h in dailyStars.history.slice(0, 7)" :key="h.id"
                class="px-2.5 py-1.5 rounded-xl bg-zinc-950 border border-zinc-800 text-center min-w-[68px]">
                <div class="text-sm">{{ getStarMeta(h.tier).icon }}</div>
                <div class="text-[10px] text-zinc-400 mt-0.5">{{ h.date }}</div>
              </div>
            </div>
          </div>
        </div>

        <div class="bg-zinc-900 border border-zinc-800 rounded-2xl p-4">
          <h2 class="text-sm font-semibold text-zinc-400 uppercase tracking-wider mb-3">Quick Check-In</h2>
          <div class="flex flex-wrap gap-2">
            <button v-for="m in ['Grounded', 'Subspace', 'Bratty', 'Tired', 'Drop']" :key="m" @click="postJournal(m)"
              class="px-3 py-1.5 rounded-xl bg-zinc-800 hover:bg-rose-950/60 border border-zinc-700 text-sm">
              {{ m }}
            </button>
          </div>
        </div>

        <div class="bg-zinc-900 border border-zinc-800 rounded-2xl p-4">
          <h2 class="text-sm font-semibold text-zinc-400 uppercase tracking-wider mb-3">Daily & Active Tasks</h2>
          <div class="space-y-2.5">
            <div v-for="t in tasks" :key="t.id" class="flex items-center justify-between p-3 rounded-xl bg-zinc-950 border border-zinc-800">
              <div>
                <div class="font-medium" :class="{ 'line-through text-zinc-500': t.isCompletedToday }">{{ t.title }}</div>
                <div class="text-xs text-zinc-400">
                  {{ t.frequency === 1 ? 'Daily' : 'One-Time' }} · <span class="text-amber-400">+{{ t.pointValue }} pts</span>
                  <span v-if="t.requiresDomApproval" class="ml-1.5 text-sky-400">· Requires Approval</span>
                  <span v-if="t.currentStreak > 0" class="ml-2 text-orange-400">🔥 {{ t.currentStreak }}d</span>
                </div>
              </div>
              <button @click="completeTask(t.id)" :disabled="t.isCompletedToday || t.isPendingApproval"
                class="px-3 py-1.5 rounded-xl text-xs font-semibold"
                :class="t.isCompletedToday ? 'bg-zinc-800 text-zinc-500' : t.isPendingApproval ? 'bg-sky-950 text-sky-300 border border-sky-700' : 'bg-rose-600 hover:bg-rose-500 text-white'">
                {{ t.isCompletedToday ? 'Done' : t.isPendingApproval ? 'Pending Approval' : 'Complete' }}
              </button>
            </div>
          </div>
        </div>

        <!-- Aftercare Quick-Request (With Customize Mode) -->
        <div class="bg-zinc-900 border border-zinc-800 rounded-2xl p-4 space-y-3">
          <div class="flex items-center justify-between">
            <h2 class="text-sm font-semibold text-zinc-400 uppercase tracking-wider">Aftercare Quick-Request</h2>
            <button @click="editingAftercareMode = !editingAftercareMode" class="px-2.5 py-1 rounded-lg bg-zinc-800 hover:bg-zinc-700 text-[11px] text-zinc-300 font-medium">
              {{ editingAftercareMode ? 'Done Editing' : 'Customize Chips' }}
            </button>
          </div>

          <div v-if="!editingAftercareMode" class="flex flex-wrap gap-2">
            <button v-for="a in aftercare" :key="a.id" @click="toggleAftercare(a.id)"
              :class="a.isRequested ? 'bg-rose-600 text-white border-rose-500' : 'bg-zinc-950 text-zinc-300 border-zinc-800'"
              class="px-3 py-1.5 rounded-xl border text-xs font-medium">
              {{ a.label }}
            </button>
          </div>

          <!-- Aftercare Add / Edit / Delete Manager -->
          <div v-else class="space-y-3 pt-1">
            <div class="flex flex-wrap gap-2">
              <input v-model="newAftercareCategory" placeholder="Category (e.g., Hydration)" class="w-32 px-3 py-1.5 rounded-xl bg-zinc-950 border border-zinc-800 text-xs" />
              <input v-model="newAftercareLabel" placeholder="New aftercare option..." class="flex-1 min-w-[160px] px-3 py-1.5 rounded-xl bg-zinc-950 border border-zinc-800 text-xs" />
              <button @click="createAftercare" class="px-3.5 py-1.5 rounded-xl bg-rose-600 hover:bg-rose-500 text-xs font-semibold">Add</button>
            </div>

            <div class="space-y-1.5">
              <div v-for="a in aftercare" :key="a.id" class="p-2.5 rounded-xl bg-zinc-950 border border-zinc-800 text-xs space-y-2">
                <div class="flex items-center justify-between gap-2">
                  <div>
                    <span class="text-rose-400 font-semibold mr-2">[{{ a.category }}]</span>
                    <span class="text-zinc-200">{{ a.label }}</span>
                  </div>
                  <div class="flex gap-1.5">
                    <button @click="startEditAftercare(a)" class="px-2 py-0.5 rounded bg-zinc-800 text-zinc-300">
                      {{ editingAftercareId === a.id ? 'Close' : 'Edit' }}
                    </button>
                    <button @click="deleteAftercare(a.id)" class="px-2 py-0.5 rounded bg-zinc-900 hover:bg-rose-900/60 text-rose-400">
                      Delete
                    </button>
                  </div>
                </div>

                <div v-if="editingAftercareId === a.id && draftAftercare[a.id]" class="flex flex-wrap gap-2 pt-2 border-t border-zinc-800">
                  <input v-model="draftAftercare[a.id].category" class="w-32 px-2 py-1 rounded bg-zinc-900 border border-zinc-800" />
                  <input v-model="draftAftercare[a.id].label" class="flex-1 px-2 py-1 rounded bg-zinc-900 border border-zinc-800" />
                  <button @click="saveAftercare(a.id)" class="px-3 py-1 rounded bg-rose-600 font-semibold">Save</button>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      <!-- TAB 2: CONSENT -->
      <section v-if="activeTab === 'consent'" class="space-y-4">
        <div class="flex flex-wrap items-center justify-between gap-2 bg-zinc-900 border border-zinc-800 rounded-2xl p-3">
          <div class="flex gap-1 bg-zinc-950 p-1 rounded-xl border border-zinc-800 text-xs">
            <button @click="consentViewMode = 'mine'" :class="consentViewMode === 'mine' ? 'bg-rose-600 text-white' : 'text-zinc-400'" class="px-3 py-1.5 rounded-lg font-semibold">
              My List ({{ myConsent.length }})
            </button>
            <button @click="consentViewMode = 'partner'" :class="consentViewMode === 'partner' ? 'bg-rose-600 text-white' : 'text-zinc-400'" class="px-3 py-1.5 rounded-lg font-semibold">
              {{ partnerMember?.roleTitle || 'Partner' }} ({{ partnerConsent.length }})
            </button>
            <button @click="consentViewMode = 'compare'" :class="consentViewMode === 'compare' ? 'bg-rose-600 text-white' : 'text-zinc-400'" class="px-3 py-1.5 rounded-lg font-semibold">
              Compare
            </button>
          </div>

          <label class="flex items-center gap-2 text-xs text-zinc-300 cursor-pointer px-2">
            <input type="checkbox" v-model="detailedConsentMode" class="accent-rose-600" />
            <span>Show Top/Bottom & Exp Badges</span>
          </label>
        </div>

        <div v-if="consentViewMode !== 'compare' && hardLimits.length" class="bg-rose-950/30 border border-rose-500/40 rounded-2xl p-4">
          <h2 class="text-xs font-bold text-rose-400 uppercase tracking-wider mb-2">
            {{ consentViewMode === 'partner' ? `${partnerMember?.roleTitle || 'Partner'}'s` : 'My' }} Pinned Hard Limits
          </h2>
          <div class="flex flex-wrap gap-1.5">
            <span v-for="hl in hardLimits" :key="hl.id" class="px-2.5 py-1 rounded-lg bg-rose-950 text-rose-200 text-xs border border-rose-800">
              {{ hl.activityName }}
            </span>
          </div>
        </div>

        <div v-if="consentViewMode === 'mine'" class="bg-zinc-900 border border-zinc-800 rounded-2xl p-4 space-y-3">
          <div class="flex flex-wrap items-center justify-between gap-2">
            <div class="text-xs font-semibold text-zinc-400 uppercase tracking-wider">Add Activity (Adds to Both Lists)</div>
            <label class="px-3 py-1.5 rounded-xl bg-zinc-800 hover:bg-zinc-700 text-xs font-semibold cursor-pointer">
              Import Google Sheet CSV
              <input type="file" accept=".csv" @change="handleCsvUpload" class="hidden" />
            </label>
          </div>
          <div class="flex flex-wrap gap-2">
            <input v-model="newConsentCategory" placeholder="Category" class="w-28 px-3 py-2 rounded-xl bg-zinc-950 border border-zinc-800 text-xs" />
            <input v-model="newConsentActivity" placeholder="Activity name..." class="flex-1 min-w-[160px] px-3 py-2 rounded-xl bg-zinc-950 border border-zinc-800 text-xs" />
            <select v-model="newConsentLevel" class="px-2.5 py-2 rounded-xl bg-zinc-950 border border-zinc-800 text-xs">
              <option :value="0">Yes</option>
              <option :value="1">Maybe</option>
              <option :value="2">No</option>
              <option :value="3">Hard Limit</option>
            </select>
            <select v-model="newConsentTopExp" class="px-2.5 py-2 rounded-xl bg-zinc-950 border border-zinc-800 text-xs">
              <option value="">Top Exp...</option>
              <option v-for="opt in expOptions.slice(1)" :key="opt.code" :value="opt.code">Top: {{ opt.label }}</option>
            </select>
            <select v-model="newConsentBotExp" class="px-2.5 py-2 rounded-xl bg-zinc-950 border border-zinc-800 text-xs">
              <option value="">Bottom Exp...</option>
              <option v-for="opt in expOptions.slice(1)" :key="opt.code" :value="opt.code">Bot: {{ opt.label }}</option>
            </select>
            <button @click="addManualConsent" class="px-4 py-2 rounded-xl bg-rose-600 hover:bg-rose-500 text-xs font-semibold">Add</button>
          </div>
        </div>

        <div class="space-y-2">
          <input v-model="consentSearch" placeholder="Search activities, categories, or notes..." class="w-full px-3.5 py-2 rounded-xl bg-zinc-900 border border-zinc-800 text-xs" />
          <div class="flex gap-1.5 overflow-x-auto pb-1">
            <button v-for="cat in categories" :key="cat" @click="consentFilter = cat"
              :class="consentFilter === cat ? 'bg-rose-600 text-white' : 'bg-zinc-900 text-zinc-400'"
              class="px-3 py-1.5 rounded-xl text-xs whitespace-nowrap">
              {{ cat }}
            </button>
          </div>
        </div>

        <!-- MY LIST OR PARTNER LIST -->
        <div v-if="consentViewMode !== 'compare'" class="space-y-2">
          <div v-if="!filteredConsent.length" class="text-center py-8 text-xs text-zinc-500 bg-zinc-900/50 rounded-2xl border border-zinc-800/60">
            No items match this filter yet. Add an activity above or import your checklist CSV.
          </div>

          <div v-for="item in filteredConsent" :key="item.id" class="p-3.5 rounded-2xl bg-zinc-900 border border-zinc-800 space-y-2.5">
            <div class="flex flex-wrap items-center justify-between gap-2">
              <div class="min-w-0 flex-1">
                <div class="flex items-center gap-2">
                  <span class="text-sm font-medium">{{ item.activityName }}</span>
                  <span class="text-[11px] text-zinc-500">{{ item.category }}</span>
                </div>
                <div v-if="item.conditionsOrNotes" class="text-xs text-amber-300/90 mt-0.5">
                  Note: {{ item.conditionsOrNotes }}
                </div>
                <div v-if="detailedConsentMode" class="flex flex-wrap gap-2 mt-1.5 text-[11px]">
                  <span class="px-2 py-0.5 rounded bg-zinc-950 border border-zinc-800 text-zinc-300">
                    Top/Give: <strong class="text-rose-400">{{ item.givingInterestCode || '—' }}</strong> · Exp: <strong class="text-sky-400">{{ item.givingExperienceCode || '—' }}</strong>
                  </span>
                  <span class="px-2 py-0.5 rounded bg-zinc-950 border border-zinc-800 text-zinc-300">
                    Bottom/Rec: <strong class="text-rose-400">{{ item.receivingInterestCode || '—' }}</strong> · Exp: <strong class="text-sky-400">{{ item.receivingExperienceCode || '—' }}</strong>
                  </span>
                </div>
              </div>

              <div class="flex items-center gap-1.5">
                <template v-if="consentViewMode === 'mine'">
                  <button v-for="(label, idx) in consentLevels" :key="label" @click="updateConsentLevel(item, idx)"
                    :class="item.level === idx ? 'bg-rose-600 text-white' : 'bg-zinc-950 text-zinc-400 hover:text-zinc-200'"
                    class="px-2 py-1 rounded-lg text-[11px] font-medium">
                    {{ label === 'HardLimit' ? 'Limit' : label }}
                  </button>
                  <button @click="toggleExpandConsent(item)" class="px-2.5 py-1 rounded-lg bg-zinc-800 hover:bg-zinc-700 text-[11px] text-zinc-200 font-medium">
                    {{ expandedConsentId === item.id ? 'Close' : 'Edit / Exp' }}
                  </button>
                </template>
                <template v-else>
                  <span :class="levelBadgeClass(item.level)" class="px-2.5 py-1 rounded-lg border text-xs font-semibold">
                    {{ levelLabel(item.level) }}
                  </span>
                </template>
              </div>
            </div>

            <div v-if="consentViewMode === 'mine' && expandedConsentId === item.id && draftEdit[item.id]" class="pt-3 border-t border-zinc-800 space-y-3">
              <div class="grid grid-cols-1 sm:grid-cols-3 gap-2 text-xs">
                <div>
                  <label class="block text-[10px] text-zinc-400 mb-1">Category (Updates Both)</label>
                  <input v-model="draftEdit[item.id].category" class="w-full px-2.5 py-1.5 rounded-lg bg-zinc-950 border border-zinc-800" />
                </div>
                <div class="sm:col-span-2">
                  <label class="block text-[10px] text-zinc-400 mb-1">Activity Name (Updates Both)</label>
                  <input v-model="draftEdit[item.id].activityName" class="w-full px-2.5 py-1.5 rounded-lg bg-zinc-950 border border-zinc-800" />
                </div>
              </div>

              <div class="grid grid-cols-2 sm:grid-cols-4 gap-2 text-xs">
                <div>
                  <label class="block text-[10px] text-zinc-400 mb-1">Top/Give Experience</label>
                  <select v-model="draftEdit[item.id].givingExperienceCode" class="w-full px-2 py-1.5 rounded-lg bg-zinc-950 border border-zinc-800">
                    <option v-for="opt in expOptions" :key="opt.code" :value="opt.code">{{ opt.label }}</option>
                  </select>
                </div>
                <div>
                  <label class="block text-[10px] text-zinc-400 mb-1">Top/Give Interest</label>
                  <select v-model="draftEdit[item.id].givingInterestCode" class="w-full px-2 py-1.5 rounded-lg bg-zinc-950 border border-zinc-800">
                    <option v-for="opt in interestOptions" :key="opt.code" :value="opt.code">{{ opt.label }}</option>
                  </select>
                </div>
                <div>
                  <label class="block text-[10px] text-zinc-400 mb-1">Bottom/Rec Experience</label>
                  <select v-model="draftEdit[item.id].receivingExperienceCode" class="w-full px-2 py-1.5 rounded-lg bg-zinc-950 border border-zinc-800">
                    <option v-for="opt in expOptions" :key="opt.code" :value="opt.code">{{ opt.label }}</option>
                  </select>
                </div>
                <div>
                  <label class="block text-[10px] text-zinc-400 mb-1">Bottom/Rec Interest</label>
                  <select v-model="draftEdit[item.id].receivingInterestCode" class="w-full px-2 py-1.5 rounded-lg bg-zinc-950 border border-zinc-800">
                    <option v-for="opt in interestOptions" :key="opt.code" :value="opt.code">{{ opt.label }}</option>
                  </select>
                </div>
              </div>

              <div>
                <label class="block text-[10px] text-zinc-400 mb-1">Conditions / Context Notes</label>
                <input v-model="draftEdit[item.id].notes" placeholder="e.g., Only with extensive warm-up..."
                  class="w-full px-3 py-1.5 rounded-xl bg-zinc-950 border border-zinc-800 text-xs" />
              </div>

              <div class="flex items-center justify-between pt-1">
                <button @click="deleteConsentItem(item)" class="px-3 py-1.5 rounded-xl bg-rose-950/50 hover:bg-rose-900/70 border border-rose-800/60 text-rose-300 text-xs font-semibold">
                  Delete Item
                </button>
                <button @click="saveConsentDetails(item)" class="px-4 py-1.5 rounded-xl bg-rose-600 hover:bg-rose-500 text-xs font-semibold">
                  Save Changes
                </button>
              </div>
            </div>
          </div>
        </div>

        <!-- PARTNER COMPARISON VIEW -->
        <div v-else class="space-y-3">
          <div class="flex flex-wrap gap-1.5">
            <button v-for="f in [
              { id: 'all', label: 'All Items' },
              { id: 'match', label: '✅ Mutual Yes' },
              { id: 'discuss', label: '💬 Needs Discussion' },
              { id: 'conflict', label: '🛑 Hard Limit' }
            ]" :key="f.id" @click="compareFilter = f.id"
              :class="compareFilter === f.id ? 'bg-zinc-800 text-white border-zinc-600' : 'bg-zinc-900 text-zinc-400 border-zinc-800'"
              class="px-3 py-1.5 rounded-xl border text-xs font-medium">
              {{ f.label }}
            </button>
          </div>

          <div v-for="row in comparisonRows" :key="row.key" class="p-3.5 rounded-2xl bg-zinc-900 border border-zinc-800 space-y-2">
            <div class="flex items-center justify-between">
              <div>
                <span class="font-medium text-sm">{{ row.activityName }}</span>
                <span class="text-xs text-zinc-500 ml-2">{{ row.category }}</span>
              </div>
              <span v-if="row.status === 'conflict'" class="text-[11px] font-bold text-rose-400">🛑 HARD LIMIT</span>
              <span v-else-if="row.status === 'match'" class="text-[11px] font-bold text-emerald-400">✅ MATCH</span>
              <span v-else-if="row.status === 'discuss'" class="text-[11px] font-bold text-amber-400">💬 DISCUSS</span>
            </div>

            <div class="grid grid-cols-1 sm:grid-cols-2 gap-2 text-xs">
              <div class="p-2.5 rounded-xl bg-zinc-950 border border-zinc-800">
                <div class="flex items-center justify-between">
                  <span class="text-zinc-400">{{ me.roleTitle }} (You)</span>
                  <span v-if="row.mine" :class="levelBadgeClass(row.mine.level)" class="px-2 py-0.5 rounded border text-[11px] font-semibold">
                    {{ levelLabel(row.mine.level) }}
                  </span>
                  <span v-else class="text-zinc-600">Not set</span>
                </div>
                <div v-if="detailedConsentMode && row.mine" class="text-[11px] text-zinc-400 mt-1">
                  Top: {{ row.mine.givingInterestCode || '—' }} (Exp {{ row.mine.givingExperienceCode || '—' }}) · Bot: {{ row.mine.receivingInterestCode || '—' }} (Exp {{ row.mine.receivingExperienceCode || '—' }})
                </div>
                <div v-if="row.mine?.conditionsOrNotes" class="text-[11px] text-amber-300/90 mt-1">{{ row.mine.conditionsOrNotes }}</div>
              </div>

              <div class="p-2.5 rounded-xl bg-zinc-950 border border-zinc-800">
                <div class="flex items-center justify-between">
                  <span class="text-zinc-400">{{ partnerMember?.roleTitle || 'Partner' }}</span>
                  <span v-if="row.theirs" :class="levelBadgeClass(row.theirs.level)" class="px-2 py-0.5 rounded border text-[11px] font-semibold">
                    {{ levelLabel(row.theirs.level) }}
                  </span>
                  <span v-else class="text-zinc-600">Not set</span>
                </div>
                <div v-if="detailedConsentMode && row.theirs" class="text-[11px] text-zinc-400 mt-1">
                  Top: {{ row.theirs.givingInterestCode || '—' }} (Exp {{ row.theirs.givingExperienceCode || '—' }}) · Bot: {{ row.theirs.receivingInterestCode || '—' }} (Exp {{ row.theirs.receivingExperienceCode || '—' }})
                </div>
                <div v-if="row.theirs?.conditionsOrNotes" class="text-[11px] text-amber-300/90 mt-1">{{ row.theirs.conditionsOrNotes }}</div>
              </div>
            </div>
          </div>
        </div>
      </section>

      <!-- TAB 3: TASKS & STANDING RULES -->
      <section v-if="activeTab === 'tasks'" class="space-y-5">
        <div class="bg-zinc-900 border border-zinc-800 rounded-2xl p-4 space-y-3">
          <h2 class="text-sm font-semibold text-zinc-400 uppercase tracking-wider">Manage & Assign Tasks</h2>
          <div v-if="me?.canAssignTasks" class="space-y-2 pb-3 border-b border-zinc-800">
            <input v-model="newTaskTitle" placeholder="New task title..." class="w-full px-3 py-2 rounded-xl bg-zinc-950 border border-zinc-800 text-sm" />
            <div class="flex flex-wrap items-center gap-2">
              <select v-model="newTaskFreq" class="px-3 py-2 rounded-xl bg-zinc-950 border border-zinc-800 text-sm">
                <option :value="1">Daily</option>
                <option :value="0">One-Time</option>
              </select>
              <input v-model="newTaskPoints" type="number" class="w-24 px-3 py-2 rounded-xl bg-zinc-950 border border-zinc-800 text-sm" />
              <label class="flex items-center gap-1.5 text-xs text-zinc-300 px-2">
                <input type="checkbox" v-model="newTaskRequiresApproval" class="accent-rose-600" />
                Requires Dom Approval
              </label>
              <button @click="createTask" class="ml-auto px-5 py-2 rounded-xl bg-rose-600 hover:bg-rose-500 text-sm font-semibold">Add Task</button>
            </div>
          </div>

          <div class="space-y-2">
            <div v-for="t in tasks" :key="t.id" class="flex items-center justify-between p-2.5 rounded-xl bg-zinc-950 border border-zinc-800 text-xs">
              <div>
                <span class="font-medium text-zinc-200">{{ t.title }}</span>
                <span class="text-zinc-500 ml-2">({{ t.frequency === 1 ? 'Daily' : 'One-Time' }} · +{{ t.pointValue }} pts)</span>
              </div>
              <button v-if="me?.canAssignTasks" @click="deleteTask(t.id)" class="px-2.5 py-1 rounded-lg bg-zinc-900 hover:bg-rose-900/60 text-rose-400">
                Delete
              </button>
            </div>
          </div>
        </div>

        <!-- Standing Rules & Protocols -->
        <div class="bg-zinc-900 border border-zinc-800 rounded-2xl p-4 space-y-4">
          <h2 class="text-sm font-semibold text-zinc-400 uppercase tracking-wider">Standing Rules & Protocols</h2>

          <div v-if="me?.canManageRules" class="p-3 rounded-xl bg-zinc-950 border border-zinc-800 space-y-2">
            <div class="text-xs font-semibold text-zinc-400">Add New Rule / Protocol</div>
            <div class="flex flex-wrap gap-2">
              <input v-model="newRuleCategory" placeholder="Category (e.g., Protocol)" class="w-36 px-3 py-1.5 rounded-lg bg-zinc-900 border border-zinc-800 text-xs" />
              <input v-model="newRuleTitle" placeholder="Rule title..." class="flex-1 min-w-[180px] px-3 py-1.5 rounded-lg bg-zinc-900 border border-zinc-800 text-xs" />
            </div>
            <div class="flex gap-2">
              <input v-model="newRuleDetails" placeholder="Rule details and expectations..." class="flex-1 px-3 py-1.5 rounded-lg bg-zinc-900 border border-zinc-800 text-xs" />
              <button @click="createRule" class="px-4 py-1.5 rounded-lg bg-rose-600 hover:bg-rose-500 text-xs font-semibold">Add Rule</button>
            </div>
          </div>

          <div class="space-y-2.5">
            <div v-for="r in rules" :key="r.id" class="p-3.5 rounded-xl bg-zinc-950 border border-zinc-800 space-y-2" :class="{ 'opacity-50': !r.isActive }">
              <div class="flex items-start justify-between gap-2">
                <div>
                  <div class="flex items-center gap-2">
                    <span class="text-xs text-rose-400 font-semibold">{{ r.category }}</span>
                    <span v-if="!r.isActive" class="text-[10px] px-1.5 py-0.5 rounded bg-zinc-800 text-zinc-400">Paused</span>
                  </div>
                  <div class="font-medium text-sm mt-0.5">{{ r.title }}</div>
                  <div class="text-xs text-zinc-400 mt-1">{{ r.details }}</div>
                </div>

                <div v-if="me?.canManageRules" class="flex items-center gap-1.5 shrink-0">
                  <button @click="toggleRuleActive(r)" class="px-2 py-1 rounded-lg bg-zinc-900 hover:bg-zinc-800 text-[11px] text-zinc-300">
                    {{ r.isActive ? 'Pause' : 'Enable' }}
                  </button>
                  <button @click="startEditRule(r)" class="px-2 py-1 rounded-lg bg-zinc-900 hover:bg-zinc-800 text-[11px] text-zinc-200">
                    {{ editingRuleId === r.id ? 'Close' : 'Edit' }}
                  </button>
                  <button @click="deleteRule(r.id)" class="px-2 py-1 rounded-lg bg-zinc-900 hover:bg-rose-900/60 text-[11px] text-rose-400">
                    Delete
                  </button>
                </div>
              </div>

              <div v-if="me?.canManageRules && editingRuleId === r.id && draftRule[r.id]" class="pt-2.5 border-t border-zinc-800 space-y-2 text-xs">
                <div class="flex flex-wrap gap-2">
                  <input v-model="draftRule[r.id].category" placeholder="Category" class="w-36 px-2.5 py-1.5 rounded-lg bg-zinc-900 border border-zinc-800" />
                  <input v-model="draftRule[r.id].title" placeholder="Title" class="flex-1 px-2.5 py-1.5 rounded-lg bg-zinc-900 border border-zinc-800" />
                </div>
                <textarea v-model="draftRule[r.id].details" rows="2" class="w-full px-2.5 py-1.5 rounded-lg bg-zinc-900 border border-zinc-800"></textarea>
                <div class="flex justify-end">
                  <button @click="saveRule(r.id)" class="px-4 py-1.5 rounded-lg bg-rose-600 hover:bg-rose-500 font-semibold">Save Rule</button>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      <!-- TAB 4: SCENES -->
      <section v-if="activeTab === 'scenes'" class="space-y-5">
        <div class="bg-zinc-900 border border-zinc-800 rounded-2xl p-4 space-y-3">
          <h2 class="text-sm font-semibold text-zinc-400 uppercase tracking-wider">Plan a Scene / Session</h2>
          <input v-model="newSceneTitle" placeholder="Scene title..." class="w-full px-3 py-2 rounded-xl bg-zinc-950 border border-zinc-800 text-sm" />
          <input v-model="newSceneGoal" placeholder="Headspace goal (optional)..." class="w-full px-3 py-2 rounded-xl bg-zinc-950 border border-zinc-800 text-sm" />
          <div class="flex items-center justify-between">
            <label class="flex items-center gap-2 text-xs text-zinc-300">
              <input type="checkbox" v-model="newSceneSafety" class="accent-rose-600" />
              Include Safety & Aftercare Essentials
            </label>
            <button @click="createScene" class="px-4 py-2 rounded-xl bg-rose-600 hover:bg-rose-500 text-xs font-semibold">Create Plan</button>
          </div>
        </div>

        <div v-for="sc in scenes" :key="sc.id" class="bg-zinc-900 border border-zinc-800 rounded-2xl p-4 space-y-4">
          <div class="flex items-start justify-between gap-2">
            <div>
              <div class="flex items-center gap-2">
                <h3 class="font-bold text-base">{{ sc.title }}</h3>
                <span class="text-[11px] px-2 py-0.5 rounded-full bg-zinc-800 text-rose-300">{{ sceneStatusMap[sc.status] }}</span>
              </div>
              <div v-if="sc.headspaceGoal" class="text-xs text-zinc-400 mt-0.5">Goal: {{ sc.headspaceGoal }}</div>
            </div>
            <div class="flex gap-1">
              <button v-if="sc.status !== 2" @click="setSceneStatus(sc.id, 2)" class="px-2.5 py-1 rounded-lg bg-emerald-700 text-xs font-semibold">Start</button>
              <button v-if="sc.status === 2" @click="setSceneStatus(sc.id, 3)" class="px-2.5 py-1 rounded-lg bg-rose-600 text-xs font-semibold">End Scene</button>
            </div>
          </div>

          <div class="space-y-2">
            <div class="text-xs font-semibold text-zinc-400 uppercase">Agreed Activities</div>
            <div class="flex flex-wrap gap-1.5">
              <span v-for="act in sc.activities" :key="act.id"
                :class="act.consentLevel === 1 ? 'border-amber-500/40 bg-amber-950/30 text-amber-200' : 'border-emerald-500/40 bg-emerald-950/30 text-emerald-200'"
                class="px-2.5 py-1 rounded-lg border text-xs">
                {{ act.activityName }} <span v-if="act.consentLevel === 1">(Maybe)</span>
              </span>
            </div>
            <div v-if="eligibleSceneConsent.length" class="flex gap-2 pt-1">
              <select v-model="selectedConsentForScene[sc.id]" class="flex-1 px-2.5 py-1.5 rounded-xl bg-zinc-950 border border-zinc-800 text-xs">
                <option value="">Select Yes/Maybe activity...</option>
                <option v-for="ec in eligibleSceneConsent" :key="ec.id" :value="ec.id">{{ ec.activityName }}</option>
              </select>
              <button @click="addActivityToScene(sc.id)" class="px-3 py-1.5 rounded-xl bg-zinc-800 text-xs font-semibold">Link</button>
            </div>
          </div>

          <div class="space-y-2">
            <div class="text-xs font-semibold text-zinc-400 uppercase">Gear & Prep Checklist</div>
            <div class="space-y-1">
              <label v-for="g in sc.gearChecklist" :key="g.id" class="flex items-center gap-2 text-xs p-2 rounded-lg bg-zinc-950 border border-zinc-800 cursor-pointer">
                <input type="checkbox" :checked="g.isPackedOrReady" @change="toggleGear(g.id)" class="accent-rose-600" />
                <span :class="{ 'line-through text-zinc-500': g.isPackedOrReady }">{{ g.itemName }}</span>
                <span class="ml-auto text-[10px] text-zinc-500">{{ g.category }}</span>
              </label>
            </div>
            <div class="flex gap-2 pt-1">
              <input v-model="newGearName[sc.id]" placeholder="Add gear item..." class="flex-1 px-2.5 py-1.5 rounded-xl bg-zinc-950 border border-zinc-800 text-xs" />
              <button @click="addGearToScene(sc.id)" class="px-3 py-1.5 rounded-xl bg-zinc-800 text-xs font-semibold">Add</button>
            </div>
          </div>
        </div>
      </section>

      <!-- TAB 5: REWARDS STORE, PUNISHMENT JAR & POINT LEDGER -->
      <section v-if="activeTab === 'rewards'" class="space-y-6">
        <div class="space-y-3">
          <h2 class="text-sm font-semibold text-zinc-400 uppercase tracking-wider">Rewards Store</h2>

          <!-- Add New Reward (Dom) -->
          <div v-if="me?.canApproveRewards" class="bg-zinc-900 border border-zinc-800 rounded-2xl p-4 space-y-2.5">
            <div class="text-xs font-semibold text-zinc-400">Add New Store Reward</div>
            <div class="flex flex-wrap gap-2">
              <input v-model="newRewardTitle" placeholder="Reward title..." class="flex-1 min-w-[180px] px-3 py-1.5 rounded-xl bg-zinc-950 border border-zinc-800 text-xs" />
              <input v-model="newRewardCost" type="number" placeholder="Points" class="w-24 px-3 py-1.5 rounded-xl bg-zinc-950 border border-zinc-800 text-xs" />
            </div>
            <div class="flex flex-wrap items-center gap-2">
              <input v-model="newRewardDesc" placeholder="Description / terms..." class="flex-1 min-w-[180px] px-3 py-1.5 rounded-xl bg-zinc-950 border border-zinc-800 text-xs" />
              <label class="flex items-center gap-1.5 text-xs text-zinc-300 px-1">
                <input type="checkbox" v-model="newRewardRepeatable" class="accent-rose-600" />
                Repeatable
              </label>
              <button @click="createReward" class="px-4 py-1.5 rounded-xl bg-rose-600 hover:bg-rose-500 text-xs font-semibold">Add Reward</button>
            </div>
          </div>

          <!-- Rewards Grid -->
          <div class="grid grid-cols-1 sm:grid-cols-2 gap-3">
            <div v-for="rw in rewards" :key="rw.id" class="p-4 rounded-2xl bg-zinc-900 border border-zinc-800 flex flex-col justify-between space-y-3">
              <div>
                <div class="flex justify-between items-start gap-2">
                  <h3 class="font-semibold text-sm">{{ rw.title }}</h3>
                  <span class="text-xs font-bold text-amber-400 whitespace-nowrap">{{ rw.pointCost }} pts</span>
                </div>
                <p class="text-xs text-zinc-400 mt-1.5">{{ rw.description }}</p>
              </div>

              <!-- Inline Edit Drawer for Reward -->
              <div v-if="me?.canApproveRewards && editingRewardId === rw.id && draftReward[rw.id]" class="pt-2.5 border-t border-zinc-800 space-y-2 text-xs">
                <input v-model="draftReward[rw.id].title" placeholder="Title" class="w-full px-2.5 py-1.5 rounded-lg bg-zinc-950 border border-zinc-800" />
                <input v-model="draftReward[rw.id].description" placeholder="Description" class="w-full px-2.5 py-1.5 rounded-lg bg-zinc-950 border border-zinc-800" />
                <div class="flex items-center justify-between gap-2">
                  <input v-model="draftReward[rw.id].pointCost" type="number" class="w-24 px-2.5 py-1 rounded-lg bg-zinc-950 border border-zinc-800" />
                  <label class="flex items-center gap-1 text-zinc-300">
                    <input type="checkbox" v-model="draftReward[rw.id].isRepeatable" class="accent-rose-600" />
                    Repeatable
                  </label>
                  <button @click="saveReward(rw.id)" class="px-3 py-1 rounded-lg bg-rose-600 font-semibold">Save</button>
                </div>
              </div>

              <div class="space-y-2">
                <button @click="redeemReward(rw.id)" class="w-full py-2 rounded-xl bg-zinc-800 hover:bg-rose-600 text-xs font-semibold transition">
                  {{ rw.isPendingFulfillment ? 'Pending Fulfillment' : 'Redeem Reward' }}
                </button>
                <div v-if="me?.canApproveRewards" class="flex justify-end gap-2 text-[11px]">
                  <button @click="startEditReward(rw)" class="text-zinc-400 hover:text-zinc-200">
                    {{ editingRewardId === rw.id ? 'Close Edit' : 'Edit' }}
                  </button>
                  <button @click="deleteReward(rw.id)" class="text-rose-400/80 hover:text-rose-300">Delete</button>
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- Punishment Jar & Active Corrections -->
        <div class="bg-zinc-900 border border-zinc-800 rounded-2xl p-4 space-y-4">
          <div class="flex flex-wrap items-center justify-between gap-2">
            <div>
              <h2 class="text-sm font-semibold text-rose-400 uppercase tracking-wider">Punishment Jar & Corrections</h2>
              <p class="text-xs text-zinc-400">{{ jarPool.length }} options in the jar</p>
            </div>
            <div class="flex items-center gap-2">
              <button v-if="me?.canAssignCorrections" @click="showJarManager = !showJarManager"
                class="px-3 py-2 rounded-xl bg-zinc-800 hover:bg-zinc-700 text-xs font-semibold text-zinc-200">
                {{ showJarManager ? 'Hide Jar Options' : 'Manage Jar Options' }}
              </button>
              <button @click="drawPunishment" class="px-3.5 py-2 rounded-xl bg-rose-600 hover:bg-rose-500 text-xs font-semibold">
                🎲 Draw Random
              </button>
            </div>
          </div>

          <!-- Active Drawn Corrections -->
          <div v-if="activeCorrections.length" class="space-y-2">
            <div class="text-xs font-semibold text-zinc-400 uppercase">Active Corrections</div>
            <div v-for="ac in activeCorrections" :key="ac.id" class="flex items-center justify-between p-3 rounded-xl bg-rose-950/20 border border-rose-500/30">
              <div>
                <div class="text-sm font-medium text-rose-200">{{ ac.title }}</div>
                <div v-if="ac.reason" class="text-xs text-zinc-400">{{ ac.reason }}</div>
              </div>
              <button @click="resolveCorrection(ac.id)" class="px-3 py-1.5 rounded-xl bg-zinc-800 hover:bg-emerald-700 text-xs font-semibold">Resolve</button>
            </div>
          </div>

          <!-- Add to Jar Quick Form -->
          <div v-if="me?.canAssignCorrections" class="flex flex-wrap gap-2 pt-1">
            <input v-model="newJarTitle" placeholder="New jar option title..." class="flex-1 min-w-[160px] px-3 py-2 rounded-xl bg-zinc-950 border border-zinc-800 text-xs" />
            <input v-model="newJarReason" placeholder="Details / duration (optional)..." class="flex-1 min-w-[160px] px-3 py-2 rounded-xl bg-zinc-950 border border-zinc-800 text-xs" />
            <button @click="addJarOption" class="px-4 py-2 rounded-xl bg-zinc-800 hover:bg-zinc-700 text-xs font-semibold">+ Jar</button>
          </div>

          <!-- Expandable Jar Pool List (Edit / Delete) -->
          <div v-if="me?.canAssignCorrections && showJarManager" class="pt-3 border-t border-zinc-800 space-y-2">
            <div class="text-xs font-semibold text-zinc-400 uppercase">Options Currently in the Jar</div>
            <div v-for="jp in jarPool" :key="jp.id" class="p-3 rounded-xl bg-zinc-950 border border-zinc-800 text-xs space-y-2">
              <div class="flex items-center justify-between gap-2">
                <div>
                  <div class="font-medium text-zinc-200">{{ jp.title }}</div>
                  <div v-if="jp.reason" class="text-zinc-400 mt-0.5">{{ jp.reason }}</div>
                </div>
                <div class="flex gap-1.5 shrink-0">
                  <button @click="startEditJar(jp)" class="px-2 py-1 rounded bg-zinc-900 hover:bg-zinc-800 text-zinc-300">
                    {{ editingJarId === jp.id ? 'Close' : 'Edit' }}
                  </button>
                  <button @click="deleteJarOption(jp.id)" class="px-2 py-1 rounded bg-zinc-900 hover:bg-rose-900/60 text-rose-400">
                    Delete
                  </button>
                </div>
              </div>

              <div v-if="editingJarId === jp.id && draftJar[jp.id]" class="flex flex-wrap gap-2 pt-2 border-t border-zinc-800">
                <input v-model="draftJar[jp.id].title" placeholder="Title" class="flex-1 px-2.5 py-1 rounded bg-zinc-900 border border-zinc-800" />
                <input v-model="draftJar[jp.id].reason" placeholder="Details" class="flex-1 px-2.5 py-1 rounded bg-zinc-900 border border-zinc-800" />
                <button @click="saveJarOption(jp.id)" class="px-3 py-1 rounded bg-rose-600 font-semibold">Save</button>
              </div>
            </div>
          </div>
        </div>

        <!-- Point History Ledger -->
        <div class="bg-zinc-900 border border-zinc-800 rounded-2xl p-4 space-y-3">
          <h2 class="text-sm font-semibold text-zinc-400 uppercase tracking-wider">Point History Ledger</h2>
          <div v-if="!transactions.length" class="text-xs text-zinc-500">No point transactions yet.</div>
          <div v-for="tx in transactions" :key="tx.id" class="flex items-center justify-between p-2.5 rounded-xl bg-zinc-950 border border-zinc-800 text-xs">
            <div>
              <span class="font-medium text-zinc-200">{{ tx.reason }}</span>
              <span class="text-zinc-500 ml-2">({{ tx.memberRole }})</span>
            </div>
            <span class="font-bold" :class="tx.delta >= 0 ? 'text-emerald-400' : 'text-rose-400'">
              {{ tx.delta >= 0 ? `+${tx.delta}` : tx.delta }} pts
            </span>
          </div>
        </div>
      </section>

      <!-- TAB 6: JOURNAL & SETTINGS -->
      <section v-if="activeTab === 'journal'" class="space-y-6">
        <!-- New Journal Entry Composer -->
        <div class="bg-zinc-900 border border-zinc-800 rounded-2xl p-4 space-y-3">
          <div class="flex flex-wrap items-center justify-between gap-2">
            <h2 class="text-sm font-semibold text-zinc-400 uppercase tracking-wider">New Journal Entry</h2>
            <select v-model="journalMood" class="px-2.5 py-1 rounded-lg bg-zinc-950 border border-zinc-800 text-xs text-rose-300">
              <option v-for="m in ['Grounded', 'Subspace', 'Bratty', 'Reflective', 'Confession', 'Gratitude', 'Tired', 'Drop']" :key="m" :value="m">
                Mood: {{ m }}
              </option>
            </select>
          </div>

          <textarea v-model="journalContent" rows="3" placeholder="Write a reflection, confession, or note..."
            class="w-full p-3 rounded-xl bg-zinc-950 border border-zinc-800 text-sm"></textarea>

          <div class="flex flex-wrap items-center justify-between gap-2">
            <label class="flex items-center gap-2 text-xs text-zinc-300 cursor-pointer">
              <input type="checkbox" v-model="journalPrivate" class="accent-rose-600" />
              <span>🔒 Private to me only (hidden from partner)</span>
            </label>
            <button @click="postJournal()" class="px-5 py-2 rounded-xl bg-rose-600 hover:bg-rose-500 text-xs font-semibold">
              Save Entry
            </button>
          </div>
        </div>

        <!-- Journal Feed -->
        <div class="space-y-3">
          <div v-for="j in journal" :key="j.id" class="p-4 rounded-2xl bg-zinc-900 border border-zinc-800 space-y-2.5">
            <div class="flex flex-wrap items-center justify-between gap-2 text-xs">
              <div class="flex items-center gap-2">
                <span class="px-2 py-0.5 rounded-full bg-zinc-800 text-zinc-200 font-semibold">{{ j.authorRole }}</span>
                <span class="text-rose-400 font-medium">{{ j.moodTag || 'Note' }}</span>
                <span v-if="j.isPrivateToAuthor" class="px-2 py-0.5 rounded bg-amber-500/20 border border-amber-500/40 text-amber-300 text-[10px] font-semibold">
                  🔒 Private
                </span>
              </div>
              <div class="flex items-center gap-2 text-zinc-400">
                <span>{{ new Date(j.createdAtUtc).toLocaleString() }}</span>
                <button v-if="j.isMine" @click="deleteJournalEntry(j.id)" class="text-rose-400/80 hover:text-rose-300 ml-1">Delete</button>
              </div>
            </div>

            <p class="text-sm text-zinc-200 whitespace-pre-line">{{ j.content }}</p>

            <!-- Existing Partner Comment Display -->
            <div v-if="j.partnerComment" class="p-2.5 rounded-xl bg-zinc-950 border-l-2 border-rose-500 text-xs text-zinc-300">
              <span class="text-rose-400 font-semibold mr-1.5">{{ partnerMember?.roleTitle || 'Partner' }} response:</span>
              <span>{{ j.partnerComment }}</span>
            </div>

            <!-- Partner Reply Input (shown on partner's non-private entries) -->
            <div v-if="!j.isMine && !j.isPrivateToAuthor" class="flex gap-2 pt-1">
              <input
                :value="draftComments[j.id] ?? j.partnerComment ?? ''"
                @input="draftComments[j.id] = $event.target.value"
                placeholder="Write a partner response or acknowledgment..."
                class="flex-1 px-3 py-1.5 rounded-xl bg-zinc-950 border border-zinc-800 text-xs" />
              <button @click="saveJournalComment(j.id)" class="px-3 py-1.5 rounded-xl bg-zinc-800 hover:bg-zinc-700 text-xs font-semibold">
                Reply
              </button>
            </div>
          </div>
        </div>

        <!-- Dynamic Roles & Permissions Settings -->
        <div v-if="me" class="bg-zinc-900 border border-zinc-800 rounded-2xl p-4 space-y-4">
          <h2 class="text-sm font-semibold text-zinc-400 uppercase tracking-wider">Dynamic Roles & Permissions</h2>

          <div v-if="me.canManageRules" class="flex gap-2">
            <input v-model="spaceNameDraft" placeholder="Dynamic space name" class="flex-1 px-3 py-2 rounded-xl bg-zinc-950 border border-zinc-800 text-xs" />
            <button @click="saveSpaceName" class="px-4 py-2 rounded-xl bg-zinc-800 hover:bg-zinc-700 text-xs font-semibold">Save Name</button>
          </div>

          <div class="space-y-3">
            <div v-for="m in me.members" :key="m.id" class="p-3.5 rounded-xl bg-zinc-950 border border-zinc-800 space-y-3 text-xs">
              <div class="flex flex-wrap items-center justify-between gap-2">
                <div class="flex flex-wrap items-center gap-2 flex-1 min-w-[200px]">
                  <input v-if="memberEdits[m.id] && (m.id === me.id || me.canManageRules)"
                    v-model="memberEdits[m.id].email"
                    placeholder="OAuth Email"
                    class="flex-1 min-w-[180px] px-2.5 py-1 rounded-lg bg-zinc-900 border border-zinc-800 text-zinc-200" />
                  <span v-else class="font-semibold text-zinc-200">{{ m.email }}</span>
                  <span v-if="m.id === me.id" class="text-[10px] px-1.5 py-0.5 rounded bg-rose-600/20 text-rose-300">You</span>
                </div>
                <div class="flex items-center gap-2">
                  <input v-if="memberEdits[m.id] && (m.id === me.id || me.canManageRules)"
                    v-model="memberEdits[m.id].roleTitle"
                    placeholder="Role title (e.g. Sir, Sub)"
                    class="w-36 px-2.5 py-1 rounded-lg bg-zinc-900 border border-zinc-800" />
                  <span v-else class="text-zinc-300 font-semibold">{{ m.roleTitle }}</span>
                </div>
              </div>

              <div v-if="me.canManageRules && memberEdits[m.id]" class="grid grid-cols-2 sm:grid-cols-4 gap-2 pt-1">
                <label class="flex items-center gap-1.5 text-zinc-300">
                  <input type="checkbox" v-model="memberEdits[m.id].canManageRules" class="accent-rose-600" />
                  Manage Rules
                </label>
                <label class="flex items-center gap-1.5 text-zinc-300">
                  <input type="checkbox" v-model="memberEdits[m.id].canAssignTasks" class="accent-rose-600" />
                  Tasks & Stars
                </label>
                <label class="flex items-center gap-1.5 text-zinc-300">
                  <input type="checkbox" v-model="memberEdits[m.id].canApproveRewards" class="accent-rose-600" />
                  Rewards Store
                </label>
                <label class="flex items-center gap-1.5 text-zinc-300">
                  <input type="checkbox" v-model="memberEdits[m.id].canAssignCorrections" class="accent-rose-600" />
                  Punishment Jar
                </label>
              </div>

              <div v-if="m.id === me.id || me.canManageRules" class="flex justify-end">
                <button @click="saveMemberSettings(m.id)" class="px-3.5 py-1.5 rounded-lg bg-rose-600 hover:bg-rose-500 font-semibold">
                  Save Role & Permissions
                </button>
              </div>
            </div>
          </div>
        </div>

        <!-- Pair Android Device (QR Code) -->
        <div class="bg-zinc-900 border border-zinc-800 rounded-2xl p-4 space-y-3">
          <h2 class="text-sm font-semibold text-zinc-400 uppercase tracking-wider">Pair Android Device (QR Code)</h2>
          <div class="flex gap-2">
            <input v-model="deviceName" class="flex-1 px-3 py-2 rounded-xl bg-zinc-950 border border-zinc-800 text-sm" />
            <button @click="generatePairingQr" class="px-4 py-2 rounded-xl bg-zinc-800 hover:bg-zinc-700 text-xs font-semibold">Generate QR</button>
          </div>
          <div v-if="pairingQrDataUrl" class="flex flex-col items-center pt-2">
            <img :src="pairingQrDataUrl" alt="Pairing QR" class="rounded-xl bg-white p-2" />
            <span class="text-xs text-zinc-400 mt-2">Expires in 3 minutes · Single-use</span>
          </div>
        </div>
      </section>
    </div>
    <!-- Persistent Bottom Navigation -->
    <nav class="fixed bottom-0 inset-x-0 bg-zinc-900/95 border-t border-zinc-800 backdrop-blur">
      <div class="w-full max-w-4xl mx-auto grid grid-cols-6 text-xs">
        <button v-for="tab in [
          { id: 'today', label: 'Today' },
          { id: 'consent', label: 'Y / N / M' },
          { id: 'tasks', label: 'Tasks' },
          { id: 'scenes', label: 'Scenes' },
          { id: 'rewards', label: 'Rewards' },
          { id: 'journal', label: 'Journal' }
        ]" :key="tab.id" @click="activeTab = tab.id"
          :class="activeTab === tab.id ? 'text-rose-400 font-bold' : 'text-zinc-400'"
          class="py-3.5 text-center">
          {{ tab.label }}
        </button>
      </div>
    </nav>
  </div>
</template>


