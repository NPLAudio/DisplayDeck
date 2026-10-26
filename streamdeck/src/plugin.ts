import streamDeck from "@elgato/streamdeck";

import { ApplyProfile } from "./actions/apply-profile";
import { AudioOutput } from "./actions/audio-output";
import { NextProfile } from "./actions/next-profile";
import { UndoLayout } from "./actions/undo-layout";

streamDeck.logger.setLevel("info");

streamDeck.actions.registerAction(new ApplyProfile());
streamDeck.actions.registerAction(new NextProfile());
streamDeck.actions.registerAction(new UndoLayout());
streamDeck.actions.registerAction(new AudioOutput());

streamDeck.connect();
