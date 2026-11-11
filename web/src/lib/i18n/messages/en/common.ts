export const common = {
  common: {
    // The name in the top bar, the page titles, the emails and the home-screen icon. Change it to your app's.
    appName: "Starter",
    back: "Back",
    cancel: "Cancel",
    closeNamed: "Close {name}",

    // Vocabulary shared by more than one feature lives here, but only once a second feature needs it.
  },

  footer: {
    copyright: "© 2026 Starter",
    privacy: "Privacy Policy",
  },

  nav: {
    signIn: "Sign in",
    notes: "Notes",
    primary: "Primary",
    account: "Account",
    logOut: "Log out",
    signOutEverywhere: "Sign out everywhere",
    signOutEverywhereTitle: "Sign out everywhere?",
    signOutEverywhereBody:
      "This signs you out here and in every other browser, as for a lost computer. You can sign in again anytime.",
    signOutEverywhereConfirm: "Sign out everywhere",
  },
} as const;
