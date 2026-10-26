import streamDeck from "@elgato/streamdeck";

import { ApplyProfile } from "./actions/apply-profile";
import { AudioDial } from "./actions/audio-dial";
import { AudioOutput } from "./actions/audio-output";
import { NextProfile } from "./actions/next-profile";
import { ProfileDial } from "./actions/profile-dial";
import { Status } from "./actions/status";
import { UndoLayout } from "./actions/undo-layout";

streamDeck.logger.setLevel("info");

// Keys
streamDeck.actions.registerAction(new ApplyProfile());
streamDeck.actions.registerAction(new NextProfile());
streamDeck.actions.registerAction(new UndoLayout()); // also a dial action
streamDeck.actions.registerAction(new AudioOutput());

// Stream Deck + dials, and the Stream Deck Neo info bar
streamDeck.actions.registerAction(new ProfileDial());
streamDeck.actions.registerAction(new AudioDial());
streamDeck.actions.registerAction(new Status());

streamDeck.connect();
